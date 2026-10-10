using Horyzont.Core.World.Terrain;

namespace Horyzont.Server.Terrain;

/// <summary>Block data of the server terrain for replication; main thread only. The game implements it over Voxel Tools.</summary>
public interface ITerrainReplicationSource
{
    /// <summary>
    /// Exports the authoritative LOD zero block as the K2 envelope: the current data of an edited block (including data not yet
    /// checkpointed) with its version, or the canonical generator block with version 0 for a block never edited.
    /// </summary>
    /// <param name="key">LOD zero key.</param>
    /// <param name="version">Version of the exported data.</param>
    /// <param name="blob">Immutable envelope.</param>
    /// <returns>False when an edited block has no data (it is never replaced by a generated block).</returns>
    bool TryExportBlock(TerrainBlockKey key, out uint version, out byte[] blob);

    /// <summary>Computes the canonical voxel digest of the generator block of the server (a block never edited).</summary>
    /// <param name="key">LOD zero key.</param>
    /// <param name="digest">The digest as <c>TerrainBlockCodec.Digest</c> defines it.</param>
    /// <returns>False when the planet is unknown.</returns>
    bool TryGetGeneratedDigest(TerrainBlockKey key, out ulong digest);
}
