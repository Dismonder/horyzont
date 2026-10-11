namespace Horyzont.Client.Terrain;

/// <summary>An immutable snapshot of the terrain replication of a remote client, for the diagnostics of the interface (K3-18).</summary>
/// <param name="Ready">Whether the startup manifest ended and its blocks arrived.</param>
/// <param name="PendingBlocks">Blocks a manifest asked for that did not arrive yet.</param>
/// <param name="BlocksReceived">Blocks taken from the server.</param>
/// <param name="BytesReceived">Bytes of those blocks.</param>
/// <param name="ManifestPages">Manifest and barrier pages received.</param>
/// <param name="StaleBlocks">Blocks ignored because the client already holds a newer version.</param>
/// <param name="RejectedBlocks">Blocks that did not decode or contradicted a manifest.</param>
/// <param name="GeneratorRepairs">Version zero blocks taken outside a manifest: repairs after a checksum difference.</param>
/// <param name="ChecksumsSent">Generated blocks whose digest was reported.</param>
/// <param name="ActivePredictions">Edits shown before the server confirmed them.</param>
/// <param name="CacheMemoryOnly">Whether the cache file is unusable and the cache lives in memory only.</param>
public sealed record TerrainReplicationStatistics(
    bool Ready, int PendingBlocks, long BlocksReceived, long BytesReceived, long ManifestPages, long StaleBlocks, long RejectedBlocks,
    long GeneratorRepairs, long ChecksumsSent, int ActivePredictions, bool CacheMemoryOnly);
