using System.IO.Hashing;

namespace Horyzont.Core.World.Terrain;

/// <summary>
/// The hash of a terrain block envelope that the terrain manifest carries (<c>TerrainSyncEntry.Hash</c>): XxHash64 of the exact
/// bytes of the K2 block envelope, so a cache entry equals the server's block exactly when version and hash both match. It is not
/// the canonical voxel digest used for generator checksums, which lives next to the engine codec.
/// </summary>
public static class TerrainBlockHash
{
    /// <summary>Hashes an envelope.</summary>
    /// <param name="envelope">The envelope bytes as served and sent.</param>
    /// <returns>The hash.</returns>
    public static ulong Compute(ReadOnlySpan<byte> envelope) => XxHash64.HashToUInt64(envelope);
}
