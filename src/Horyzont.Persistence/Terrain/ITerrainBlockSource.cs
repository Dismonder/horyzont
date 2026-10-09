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
}
