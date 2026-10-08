using System.Buffers.Binary;
using System.IO.Hashing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Hashing;

/// <summary>
/// XxHash64 of a canonical byte stream of the state: exactly what checkpoints plus journal replay restore.
/// Excludes CurrentTick, player kinematics and the pose of <see cref="ConstructMode.Ship"/> constructs (they change
/// outside the journal) and terrain voxels (outside Core; only block versions are hashed). Floats are hashed bitwise.
/// </summary>
public static class StateHasher
{
    /// <summary>Hashes a state.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The hash.</returns>
    public static ulong Hash(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var hash = new XxHash64();
        Span<byte> guid = stackalloc byte[16];
        state.Id.Value.TryWriteBytes(guid);
        hash.Append(guid);
        U64(hash, state.GalaxySeed);
        U64(hash, state.EntityIds.NextValue);
        U64(hash, state.PlayerIds.NextValue);
        U64(hash, state.LastOpId.Value);

        Registry(hash, state.BlockTypes.Entries);
        Registry(hash, state.Materials.Entries);

        U32(hash, (uint)state.Planets.All.Count);
        foreach (var planet in state.Planets.All)
        {
            U64(hash, planet.Id.Value);
            U64(hash, planet.System.Value);
            U64(hash, planet.Seed);
            Text(hash, planet.GeneratorProfile);
            F32(hash, planet.RadiusMeters);
            F32(hash, planet.VoxelSizeMeters);
            F32(hash, planet.Gravity);
            F32(hash, planet.AtmosphereHeightMeters);
            F32(hash, planet.CoreDepthMeters);
            F32(hash, planet.DayLengthSeconds);
        }

        U32(hash, (uint)state.Constructs.Count);
        foreach (var construct in state.Constructs.All)
        {
            U64(hash, construct.Id.Value);
            Frame(hash, construct.Parent);
            U32(hash, construct.ParentSeq);
            U32(hash, (uint)construct.Mode);
            if (construct.Mode == ConstructMode.Static)
            {
                Vec(hash, construct.Origin);
                Quat(hash, construct.Rotation);
            }

            U32(hash, construct.Version);
            U32(hash, (uint)construct.ChunkCount);
            foreach (var chunk in construct.Chunks)
            {
                U32(hash, (uint)chunk.Key.X);
                U32(hash, (uint)chunk.Key.Y);
                U32(hash, (uint)chunk.Key.Z);
                Blocks(hash, chunk.Value.Blocks);
            }
        }

        U32(hash, (uint)state.TerrainVersions.Count);
        foreach (var version in state.TerrainVersions.All)
        {
            U64(hash, version.Key.Planet.Value);
            U32(hash, version.Key.Lod);
            U32(hash, (uint)version.Key.X);
            U32(hash, (uint)version.Key.Y);
            U32(hash, (uint)version.Key.Z);
            U32(hash, version.Version);
        }

        U32(hash, (uint)state.Players.Count);
        foreach (var player in state.Players.All)
        {
            U64(hash, player.Player.Value);
            U32(hash, (uint)player.Identity.Platform);
            U64(hash, player.Identity.Value);
            Text(hash, player.DisplayName);
            U64(hash, player.Entity.Value);
            Frame(hash, player.Frame);
            U32(hash, player.ParentSeq);
            if (player.Seat is { } seat)
            {
                U32(hash, 1);
                U32(hash, (uint)seat.X);
                U32(hash, (uint)seat.Y);
                U32(hash, (uint)seat.Z);
            }
            else
            {
                U32(hash, 0);
            }

            U32(hash, player.IsOnline ? 1u : 0u);
        }

        return hash.GetCurrentHashAsUInt64();
    }

    private static void Registry(XxHash64 hash, IReadOnlyList<RegistryEntry> entries)
    {
        U32(hash, (uint)entries.Count);
        foreach (var entry in entries)
        {
            U32(hash, entry.Id);
            Text(hash, entry.Key);
        }
    }

    private static void Frame(XxHash64 hash, FrameRef frame)
    {
        U32(hash, (uint)frame.Kind);
        U64(hash, frame.Id);
    }

    private static void Blocks(XxHash64 hash, ReadOnlySpan<uint> blocks)
    {
        if (BitConverter.IsLittleEndian)
        {
            hash.Append(MemoryMarshal.AsBytes(blocks));
            return;
        }

        foreach (var value in blocks)
        {
            U32(hash, value);
        }
    }

    private static void Text(XxHash64 hash, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        U32(hash, (uint)bytes.Length);
        hash.Append(bytes);
    }

    private static void Vec(XxHash64 hash, Vector3 v)
    {
        F32(hash, v.X);
        F32(hash, v.Y);
        F32(hash, v.Z);
    }

    private static void Quat(XxHash64 hash, Quaternion q)
    {
        F32(hash, q.X);
        F32(hash, q.Y);
        F32(hash, q.Z);
        F32(hash, q.W);
    }

    private static void F32(XxHash64 hash, float value) => U32(hash, BitConverter.SingleToUInt32Bits(value));

    private static void U32(XxHash64 hash, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        hash.Append(bytes);
    }

    private static void U64(XxHash64 hash, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        hash.Append(bytes);
    }
}
