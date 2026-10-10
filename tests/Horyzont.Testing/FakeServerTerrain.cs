using Horyzont.Core.Checkpoints;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Writing;
using Horyzont.Server;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Testing;

/// <summary>
/// <see cref="IServerTerrain"/> over a <see cref="FakeTerrainService"/>, a real <see cref="TerrainBlockStore"/> and the real
/// <see cref="ServerTerrainBlocks"/> flow of K2-09: every edited block is recorded right after the edit, missing blocks are read from
/// the buffer, checkpoints carry the pending blocks. Tests steer the shutdown and the early-checkpoint signal.
/// </summary>
public sealed class FakeServerTerrain : IServerTerrain, IDisposable
{
    private readonly TerrainBlockStore _store;
    private readonly ServerTerrainBlocks _blocks;
    private TaskCompletionSource<bool>? _hang;
    private bool _storeClosed;

    public FakeServerTerrain(string worldPath, Core.World.WorldState state, TimeProvider time)
    {
        _store = new TerrainBlockStore(worldPath);
        _blocks = new ServerTerrainBlocks(_store, state.TerrainVersions.All, new TerrainBufferOptions(), time, NullLogger<ServerTerrainBlocks>.Instance);
        Terrain = new FakeTerrainService { BlockSource = ReadBlock };
        Terrain.AfterApplyEdit = (_, versions) =>
        {
            foreach (var version in versions)
            {
                _blocks.Record(version.Key, version.Version, Terrain.GetBlockData(version.Key));
            }
        };
    }

    /// <summary>Gets the fake terrain service the simulation edits.</summary>
    public FakeTerrainService Terrain { get; }

    ITerrainService IServerTerrain.Service => Terrain;

    /// <summary>Gets or sets a value that makes <see cref="NeedsEarlyCheckpoint"/> true.</summary>
    public bool ForceEarlyCheckpoint { get; set; }

    /// <inheritdoc />
    public bool NeedsEarlyCheckpoint => ForceEarlyCheckpoint || _blocks.NeedsEarlyCheckpoint;

    /// <summary>Gets the checkpoints the buffer saw committed.</summary>
    public int CommittedCheckpoints { get; private set; }

    /// <summary>Gets the checkpoints the buffer saw failed.</summary>
    public int FailedCheckpoints { get; private set; }

    /// <summary>Gets the requests built, in order.</summary>
    public List<CheckpointRequest> Requests { get; } = [];

    /// <summary>Gets the journal entries replayed.</summary>
    public int ReplayedEntries { get; private set; }

    /// <summary>Gets the number of calls to <see cref="ShutdownAsync"/>.</summary>
    public int ShutdownCalls { get; private set; }

    /// <summary>Gets or sets what <see cref="ShutdownAsync"/> reports: false leaves the store open, as when reads still run.</summary>
    public bool CloseSucceeds { get; set; } = true;

    /// <summary>Gets or sets a callback run first thing in <see cref="ShutdownAsync"/> (tests record the order).</summary>
    public Action? OnShutdown { get; set; }

    /// <summary>Gets or sets a value that makes <see cref="ShutdownAsync"/> wait until <see cref="ReleaseShutdown"/>.</summary>
    public bool HangShutdown { get; set; }

    /// <summary>Gets or sets a callback run in <see cref="ReplayAsync"/> before the entries (tests inspect or fail the replay).</summary>
    public Action<IReadOnlyList<JournalEntry>>? OnReplay { get; set; }

    /// <inheritdoc />
    public CheckpointRequest CreateCheckpointRequest(WorldCheckpoint core)
    {
        var request = _blocks.CreateCheckpointRequest(core);
        Requests.Add(request);
        return request;
    }

    /// <inheritdoc />
    public void OnCheckpointCommitted(CheckpointRequest request)
    {
        CommittedCheckpoints++;
        _blocks.OnCheckpointCommitted(request);
    }

    /// <inheritdoc />
    public void OnCheckpointFailed(CheckpointRequest request)
    {
        FailedCheckpoints++;
        _blocks.OnCheckpointFailed(request);
    }

    /// <inheritdoc />
    public Task ReplayAsync(ServerSimulation simulation, IReadOnlyList<JournalEntry> tail, CancellationToken ct)
    {
        OnReplay?.Invoke(tail);
        foreach (var entry in tail)
        {
            ct.ThrowIfCancellationRequested();
            simulation.Replay(entry);
            ReplayedEntries++;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> ShutdownAsync()
    {
        ShutdownCalls++;
        OnShutdown?.Invoke();
        if (HangShutdown)
        {
            _hang = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _hang.Task.ConfigureAwait(false);
        }

        if (!CloseSucceeds)
        {
            return false;
        }

        var closed = await _blocks.CloseAsync().ConfigureAwait(false);
        if (closed)
        {
            CloseStore();
        }

        return closed;
    }

    /// <summary>Lets a hanging <see cref="ShutdownAsync"/> go on.</summary>
    public void ReleaseShutdown() => _hang?.TrySetResult(true);

    /// <summary>Closes the store for real (tests that made <see cref="CloseSucceeds"/> false release the file this way).</summary>
    public void CloseStore()
    {
        if (!_storeClosed)
        {
            _storeClosed = true;
            _store.Close();
        }
    }

    /// <inheritdoc />
    public void Dispose() => CloseStore();

    /// <summary>Gets a value indicating whether the store was closed (by <see cref="ShutdownAsync"/> or <see cref="CloseStore"/>).</summary>
    public bool IsStoreClosed => _storeClosed;

    private byte[]? ReadBlock(TerrainBlockKey key) =>
        _blocks.TryLoad(key, out _, out var blob) switch
        {
            TerrainBlockLookup.Found => blob,
            TerrainBlockLookup.NotEdited => null,
            _ => throw new InvalidOperationException($"Terrain block {key} is unavailable."),
        };
}
