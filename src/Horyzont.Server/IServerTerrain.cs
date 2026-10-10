using Horyzont.Core.Checkpoints;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Writing;

namespace Horyzont.Server;

/// <summary>What <see cref="ServerRuntime"/> needs from the terrain of the server; the game implements it over Voxel Tools.</summary>
public interface IServerTerrain
{
    /// <summary>Gets the terrain service the simulation edits and queries; main thread only.</summary>
    ITerrainService Service { get; }

    /// <summary>Gets a value indicating whether the buffer of unsaved terrain blocks asks for a checkpoint before the interval ends.</summary>
    bool NeedsEarlyCheckpoint { get; }

    /// <summary>Builds the checkpoint request for the captured core changes, with the voxel data of the changed blocks.</summary>
    /// <param name="core">Result of <c>WorldState.CaptureCheckpoint</c>.</param>
    /// <returns>The request for <c>PersistenceWriter.CheckpointAsync</c>.</returns>
    /// <exception cref="InvalidOperationException">The buffer cannot supply a block of the captured versions (a bug).</exception>
    CheckpointRequest CreateCheckpointRequest(WorldCheckpoint core);

    /// <summary>Tells the terrain buffer that the checkpoint was written.</summary>
    /// <param name="request">The committed request.</param>
    void OnCheckpointCommitted(CheckpointRequest request);

    /// <summary>Tells the terrain buffer that the checkpoint was not written; its blocks stay pending.</summary>
    /// <param name="request">The failed request.</param>
    void OnCheckpointFailed(CheckpointRequest request);

    /// <summary>Replays the journal tail on the simulation, loading terrain areas before the edits.</summary>
    /// <param name="simulation">The simulation over the loaded state.</param>
    /// <param name="tail">Journal entries after the last checkpoint.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>A task that completes when the whole tail is replayed.</returns>
    /// <exception cref="ReplayFailedException">An entry is invalid or breaks a precondition.</exception>
    /// <exception cref="TimeoutException">A terrain area did not load in time.</exception>
    Task ReplayAsync(ServerSimulation simulation, IReadOnlyList<JournalEntry> tail, CancellationToken ct);

    /// <summary>
    /// Shuts the terrain down: <c>VoxelTerrainService.ShutdownAsync</c>, then <c>ServerTerrainBlocks.CloseAsync</c>, then
    /// <c>TerrainBlockStore.Close()</c> only when <c>CloseAsync</c> returned true.
    /// </summary>
    /// <returns>False when the store was left open because reads were still running (D8); true when it was closed.</returns>
    Task<bool> ShutdownAsync();
}
