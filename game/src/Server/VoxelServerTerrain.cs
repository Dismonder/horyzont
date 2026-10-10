using Godot;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Terrain;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Writing;
using Horyzont.Server;

namespace Horyzont.Game.Server;

/// <summary>
/// <see cref="IServerTerrain"/> over Voxel Tools (K2-09): the terrain service with the planets under one parent node, the buffer of
/// edited blocks, the block store and the journal replay with area loading. Main thread, except the buffer reads of the streams.
/// </summary>
public sealed class VoxelServerTerrain : IServerTerrain
{
    private readonly ServerTerrainBlocks _blocks;
    private readonly TerrainBlockStore _store;
    private readonly IReadOnlyList<PlanetTerrain> _nodes;
    private readonly TimeProvider _time;

    /// <summary>Creates the wrapper; the factory builds the parts.</summary>
    /// <param name="service">Terrain service with the planets added.</param>
    /// <param name="blocks">Edited blocks of the world.</param>
    /// <param name="store">Block store of the world file.</param>
    /// <param name="nodes">The planet nodes (in the scene tree under the server node).</param>
    /// <param name="time">Clock of the area loader.</param>
    public VoxelServerTerrain(
        VoxelTerrainService service, ServerTerrainBlocks blocks, TerrainBlockStore store, IReadOnlyList<PlanetTerrain> nodes, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(time);
        Voxel = service;
        _blocks = blocks;
        _store = store;
        _nodes = nodes;
        _time = time;
    }

    /// <summary>Gets the terrain service (the host's local client reads the same planets through it).</summary>
    public VoxelTerrainService Voxel { get; }

    /// <inheritdoc />
    public ITerrainService Service => Voxel;

    /// <inheritdoc />
    public bool NeedsEarlyCheckpoint => _blocks.NeedsEarlyCheckpoint;

    /// <inheritdoc />
    public CheckpointRequest CreateCheckpointRequest(WorldCheckpoint core) => _blocks.CreateCheckpointRequest(core);

    /// <inheritdoc />
    public void OnCheckpointCommitted(CheckpointRequest request) => _blocks.OnCheckpointCommitted(request);

    /// <inheritdoc />
    public void OnCheckpointFailed(CheckpointRequest request) => _blocks.OnCheckpointFailed(request);

    /// <inheritdoc />
    /// <remarks>
    /// The semantics of the tail were checked by the server runtime before it created this terrain, so the check of
    /// <see cref="TerrainReplay"/> runs against the current semantics and cannot report a mismatch.
    /// </remarks>
    public async Task ReplayAsync(ServerSimulation simulation, IReadOnlyList<JournalEntry> tail, CancellationToken ct)
    {
        var loader = new TerrainAreaLoader(Voxel, _time, TerrainAreaLoader.DefaultDeadline);
        var check = await TerrainReplay.ReplayAsync(simulation, tail, loader, TerrainEditSemantics.Current, ct).ConfigureAwait(true);
        if (check.IsMismatch)
        {
            throw new InvalidOperationException($"The journal tail does not match the terrain semantics after the runtime accepted it: {check.Describe()}");
        }
    }

    /// <inheritdoc />
    public async Task<bool> ShutdownAsync()
    {
        var drained = await Voxel.ShutdownAsync().ConfigureAwait(true);
        if (drained)
        {
            _store.Close();
        }

        foreach (var node in _nodes)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                node.GetParent()?.RemoveChild(node);
                node.QueueFree();
            }
        }

        if (drained)
        {
            Voxel.Dispose();
        }

        return drained;
    }
}
