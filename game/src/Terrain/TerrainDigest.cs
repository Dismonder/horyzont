using System.Buffers.Binary;
using System.IO.Hashing;

using Horyzont.Core.World.Terrain;

namespace Horyzont.Game.Terrain;

/// <summary>
/// Digest of the voxel data of terrain blocks: XxHash64 of every (key, version, voxel digest) in <see cref="TerrainBlockKey"/>
/// order. Used to compare live, replayed and checkpointed terrain (K2-09), client checksums (K3-16) and identity checks (K4-19).
/// </summary>
public static class TerrainDigest
{
    private const int EntryBytes = 8 + 1 + 4 + 4 + 4 + 4 + 8;

    /// <summary>Computes the digest; the input order does not matter.</summary>
    /// <param name="blocks">Blocks with their version and <see cref="TerrainBlockCodec.Digest"/>.</param>
    /// <returns>The digest.</returns>
    /// <exception cref="ArgumentException">A key repeats.</exception>
    public static ulong Compute(IEnumerable<(TerrainBlockKey Key, uint Version, ulong VoxelDigest)> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var sorted = new List<(TerrainBlockKey Key, uint Version, ulong VoxelDigest)>(blocks);
        sorted.Sort(static (a, b) => a.Key.CompareTo(b.Key));
        var hash = new XxHash64();
        Span<byte> entry = stackalloc byte[EntryBytes];
        for (var i = 0; i < sorted.Count; i++)
        {
            var (key, version, voxels) = sorted[i];
            if (i > 0 && sorted[i - 1].Key == key)
            {
                throw new ArgumentException($"Terrain block {key} appears twice.", nameof(blocks));
            }

            BinaryPrimitives.WriteUInt64LittleEndian(entry, key.Planet.Value);
            entry[8] = key.Lod;
            BinaryPrimitives.WriteInt32LittleEndian(entry[9..], key.X);
            BinaryPrimitives.WriteInt32LittleEndian(entry[13..], key.Y);
            BinaryPrimitives.WriteInt32LittleEndian(entry[17..], key.Z);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[21..], version);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[25..], voxels);
            hash.Append(entry);
        }

        return hash.GetCurrentHashAsUInt64();
    }
}
