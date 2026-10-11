using System.Buffers.Binary;
using System.Globalization;
using System.IO.Hashing;

using Horyzont.Core.World.Terrain;

namespace Horyzont.Bots.Digests;

/// <summary>
/// Digest of terrain block versions: XxHash64 of every (key, version) in <see cref="TerrainBlockKey"/> order. Bots without an engine
/// hold no voxels, so for them the terrain is the version of each block plus the hash of the envelope they received.
/// </summary>
public static class TerrainVersionDigest
{
    private const int EntryBytes = 8 + 1 + 4 + 4 + 4 + 4;

    /// <summary>Computes the digest; the input order does not matter.</summary>
    /// <param name="versions">Block versions.</param>
    /// <returns>The digest.</returns>
    /// <exception cref="ArgumentException">A key repeats.</exception>
    public static ulong Compute(IEnumerable<TerrainBlockVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        var sorted = new List<TerrainBlockVersion>(versions);
        sorted.Sort(static (a, b) => a.Key.CompareTo(b.Key));
        var hash = new XxHash64();
        Span<byte> entry = stackalloc byte[EntryBytes];
        for (var i = 0; i < sorted.Count; i++)
        {
            var (key, version) = sorted[i];
            if (i > 0 && sorted[i - 1].Key == key)
            {
                throw new ArgumentException($"Terrain block {key} appears twice.", nameof(versions));
            }

            BinaryPrimitives.WriteUInt64LittleEndian(entry, key.Planet.Value);
            entry[8] = key.Lod;
            BinaryPrimitives.WriteInt32LittleEndian(entry[9..], key.X);
            BinaryPrimitives.WriteInt32LittleEndian(entry[13..], key.Y);
            BinaryPrimitives.WriteInt32LittleEndian(entry[17..], key.Z);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[21..], version);
            hash.Append(entry);
        }

        return hash.GetCurrentHashAsUInt64();
    }

    /// <summary>Formats a key as <c>planet/lod/x/y/z</c>.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The text.</returns>
    public static string FormatKey(TerrainBlockKey key) =>
        string.Create(CultureInfo.InvariantCulture, $"{key.Planet.Value}/{key.Lod}/{key.X}/{key.Y}/{key.Z}");

    /// <summary>Makes the dump entry of a terrain block.</summary>
    /// <param name="key">LOD zero key.</param>
    /// <param name="version">Version.</param>
    /// <param name="envelopeHash"><see cref="TerrainBlockHash"/> of the envelope; 0 when unknown.</param>
    /// <param name="voxels">Voxel digest of the block; null when the writer holds no voxels.</param>
    /// <returns>The entry.</returns>
    public static DigestEntry Entry(TerrainBlockKey key, uint version, ulong envelopeHash, ulong? voxels = null) =>
        new(DigestKind.Terrain, FormatKey(key), version, envelopeHash, ((long)key.Planet.Value, key.Lod, key.X, key.Y, key.Z), Voxels: voxels);
}
