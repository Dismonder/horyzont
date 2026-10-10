using Horyzont.Core.World.Terrain;

namespace Horyzont.Persistence.Terrain;

/// <summary>
/// Saved and pending terrain blocks for a voxel stream (the server's <see cref="ServerTerrainBlocks"/>; the client's cache in K3-16).
/// Thread-safe: called from Voxel Tools worker threads.
/// </summary>
public interface ITerrainBlockSource
{
    /// <summary>Reads the current version and blob of a LOD 0 block.</summary>
    /// <param name="key">Block key.</param>
    /// <param name="version">The version when <see cref="TerrainBlockLookup.Found"/>; otherwise 0.</param>
    /// <param name="blob">
    /// The terrain block blob (<c>StateCodec.EncodeTerrainBlockBlob</c>) when <see cref="TerrainBlockLookup.Found"/>; otherwise empty.
    /// The array is shared and must not be modified.
    /// </param>
    /// <returns>Whether the block was found, never edited, or cannot be served.</returns>
    TerrainBlockLookup TryLoad(TerrainBlockKey key, out uint version, out byte[] blob);

    /// <summary>
    /// <see cref="TryLoad(TerrainBlockKey, out uint, out byte[])"/> for a reader that only derives data from the block: a source that
    /// can ask for missing blocks again (the client cache) does not do so when <paramref name="refetch"/> is false.
    /// </summary>
    /// <param name="key">Block key.</param>
    /// <param name="refetch">Whether this look may trigger a refetch.</param>
    /// <param name="version">The version when found; otherwise 0.</param>
    /// <param name="blob">The blob when found; otherwise empty.</param>
    /// <returns>Whether the block was found, never edited, or cannot be served.</returns>
    TerrainBlockLookup TryLoad(TerrainBlockKey key, bool refetch, out uint version, out byte[] blob) => TryLoad(key, out version, out blob);
}
