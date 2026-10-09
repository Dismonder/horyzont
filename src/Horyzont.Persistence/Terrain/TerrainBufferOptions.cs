namespace Horyzont.Persistence.Terrain;

/// <summary>Limits of the buffer of terrain blocks changed since the last committed checkpoint.</summary>
public sealed record TerrainBufferOptions
{
    /// <summary>
    /// Pending blocks at which <see cref="ServerTerrainBlocks.NeedsEarlyCheckpoint"/> turns true; the server loop then requests a
    /// checkpoint when none is in flight.
    /// </summary>
    public int EarlyCheckpointBlocks { get; init; } = 4096;

    /// <summary>
    /// Pending blocks at which <see cref="ServerTerrainBlocks.IsSaturated"/> turns true; the terrain then reports areas as not
    /// loaded, so new terrain edits are rejected until a checkpoint commits.
    /// </summary>
    public int MaxPendingBlocks { get; init; } = 16384;

    /// <summary>Longest wait of <see cref="ServerTerrainBlocks.CloseAsync"/> for reads in progress.</summary>
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
