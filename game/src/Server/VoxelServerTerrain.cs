using Godot;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Terrain;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Writing;
using Horyzont.Server;
using Horyzont.Server.Terrain;

namespace Horyzont.Game.Server;

/// <summary>
/// <see cref="IServerTerrain"/> over Voxel Tools (K2-09): the terrain service with the planets under one parent node, the buffer of
/// edited blocks, the block store and the journal replay with area loading. Main thread, except the buffer reads of the streams.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The codec and the generated block sources are disposed by ShutdownAsync, the last step of the terrain shutdown.")]
public sealed class VoxelServerTerrain : IServerTerrain, ITerrainReplicationSource
{
    private readonly ServerTerrainBlocks _blocks;
    private readonly TerrainBlockStore _store;
    private readonly IReadOnlyList<PlanetTerrain> _nodes;
    private readonly TimeProvider _time;
    private readonly TerrainBlockCodec _codec = new();
    private readonly Dictionary<ulong, GeneratedBlockSource> _generated = [];

    /// <summary>Creates the wrapper; the factory builds the parts.</summary>
    /// <param name="service">Terrain service with the planets added.</param>
    /// <param name="blocks">Edited blocks of the world.</param>
    /// <param name="store">Block store of the world file.</param>
    /// <param name="nodes">The planet nodes (in the scene tree under the server node).</param>
    /// <param name="time">Clock of the area loader.</param>
    /// <param name="derived">Persistent mipmaps of the planets; stepped by the game and closed in step 7a of the shutdown.</param>
    public VoxelServerTerrain(
        VoxelTerrainService service, ServerTerrainBlocks blocks, TerrainBlockStore store, IReadOnlyList<PlanetTerrain> nodes, TimeProvider time,
        DerivedTerrain? derived = null)
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
        Derived = derived;
    }

    /// <summary>Gets the persistent mipmaps of the edited terrain (K3-16); null when the terrain has none.</summary>
    public DerivedTerrain? Derived { get; }

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
    /// <remarks>
    /// Reads the current <see cref="ServerTerrainBlocks"/> (the buffer in front of the store, so data not yet checkpointed is included)
    /// for an edited block; a block never edited is the canonical generator block with version 0. An edited block without data is
    /// never replaced by a generated one. Main thread; a generator block is made on the spot, so the caller bounds how many it asks for.
    /// </remarks>
    public bool TryExportBlock(TerrainBlockKey key, out uint version, out byte[] blob)
    {
        version = 0;
        blob = [];
        if (key.Lod != 0)
        {
            return false;
        }

        switch (_blocks.TryLoad(key, out version, out blob))
        {
            case TerrainBlockLookup.Found:
                return true;
            case TerrainBlockLookup.NotEdited when TryGetGenerated(key.Planet.Value, out var generated):
                version = 0;
                blob = generated.Encode(key);
                return true;
            default:
                version = 0;
                blob = [];
                return false;
        }
    }

    /// <inheritdoc />
    public bool TryGetGeneratedDigest(TerrainBlockKey key, out ulong digest)
    {
        digest = 0;
        if (key.Lod != 0 || !TryGetGenerated(key.Planet.Value, out var generated))
        {
            return false;
        }

        digest = generated.Digest(key);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ShutdownAsync()
    {
        var drained = await Voxel.ShutdownAsync().ConfigureAwait(true);
        if (drained)
        {
            // D8: readers that did not finish may still use the generated sources and the codec, so they go only after a drain.
            foreach (var source in _generated.Values)
            {
                source.Dispose();
            }

            _generated.Clear();
            _codec.Dispose();
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

    private bool TryGetGenerated(ulong planet, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out GeneratedBlockSource? source)
    {
        if (_generated.TryGetValue(planet, out source))
        {
            return true;
        }

        foreach (var node in _nodes)
        {
            if (node.Planet.Value == planet && GodotObject.IsInstanceValid(node) && node.Terrain.Generator is { } generator)
            {
                source = new GeneratedBlockSource(generator, _codec);
                _generated.Add(planet, source);
                return true;
            }
        }

        source = null;
        return false;
    }
}
