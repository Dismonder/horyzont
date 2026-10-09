using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Writing;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests.Support;

/// <summary>
/// The server loop of K2-09 in miniature: a loaded world, <see cref="ServerTerrainBlocks"/> over its store, a fake terrain that
/// reads missing blocks from the buffer and records every edited block right after the edit, the writer, and the checkpoint flow
/// (capture → request from the buffer → write → commit or requeue on both sides).
/// </summary>
internal sealed class TerrainServer : IAsyncDisposable
{
    private TerrainServer(
        string path, FakeTimeProvider time, LoadedWorld loaded, TerrainBlockStore store, ServerTerrainBlocks blocks, FakeTerrainService terrain,
        PersistenceWriter writer, ServerSimulation sim)
    {
        DbPath = path;
        Time = time;
        Loaded = loaded;
        Store = store;
        Blocks = blocks;
        Terrain = terrain;
        Writer = writer;
        Sim = sim;
    }

    public string DbPath { get; }

    public FakeTimeProvider Time { get; }

    public LoadedWorld Loaded { get; }

    public TerrainBlockStore Store { get; }

    public ServerTerrainBlocks Blocks { get; }

    public FakeTerrainService Terrain { get; }

    public PersistenceWriter Writer { get; }

    public ServerSimulation Sim { get; }

    public Core.World.WorldState State => Sim.State;

    /// <summary>Every recorded (key, version) with its blob: the oracle of concurrent readers.</summary>
    public ConcurrentDictionary<(TerrainBlockKey Key, uint Version), byte[]> Recorded { get; } = new();

    /// <summary>The newest version per key whose <c>Record</c> call has returned (readers on other threads may rely on it).</summary>
    public ConcurrentDictionary<TerrainBlockKey, uint> Published { get; } = new();

    public bool FaultsExpected { get; set; }

    public static TerrainServer CreateNew(string path, IPersistenceFaults? faults = null, TerrainBufferOptions? options = null)
    {
        var time = new FakeTimeProvider();
        WorldCreator.Create(path, WorldFactory.CreateDefault(), WorldHarness.Options, time);
        return Open(path, faults, options, time);
    }

    /// <summary>Loads, creates the buffer from the loaded versions, starts the writer and replays the tail.</summary>
    public static TerrainServer Open(string path, IPersistenceFaults? faults = null, TerrainBufferOptions? options = null, FakeTimeProvider? time = null)
    {
        time ??= new FakeTimeProvider();
        var loaded = WorldLoader.Load(path, WorldHarness.LoadOptions(path), time, NullLogger.Instance);
        var store = new TerrainBlockStore(path);
        var blocks = new ServerTerrainBlocks(
            store, loaded.State.TerrainVersions.All, options ?? new TerrainBufferOptions(), time, NullLogger<ServerTerrainBlocks>.Instance);
        var terrain = new FakeTerrainService();
        var writer = new PersistenceWriter(
            WorldDatabase.Open(path), WorldHarness.Options, time, faults ?? NoFaults.Instance, NullLogger<PersistenceWriter>.Instance);
        var sim = new ServerSimulation(loaded.State, terrain, new JournalSink(writer), new GameRules(), NullLogger<ServerSimulation>.Instance);
        var server = new TerrainServer(path, time, loaded, store, blocks, terrain, writer, sim);
        terrain.BlockSource = server.ReadBlock;
        terrain.AfterApplyEdit = (_, versions) =>
        {
            foreach (var version in versions)
            {
                var blob = terrain.GetBlockData(version.Key);
                server.Recorded[(version.Key, version.Version)] = blob;
                blocks.Record(version.Key, version.Version, blob);
                server.Published[version.Key] = version.Version;
            }
        };

        foreach (var entry in loaded.JournalTail)
        {
            sim.Replay(entry);
        }

        return server;
    }

    /// <summary>Captures a checkpoint, builds its request from the buffer and hands it to the writer.</summary>
    public Task<bool> RequestCheckpoint()
    {
        var core = State.CaptureCheckpoint();
        CheckpointRequest request;
        try
        {
            request = Blocks.CreateCheckpointRequest(core);
        }
        catch (InvalidOperationException)
        {
            State.RequeueAfterFailedCheckpoint(core);
            throw;
        }

        return Writer.CheckpointAsync(request);
    }

    public async Task<bool> CheckpointAndPump()
    {
        var result = await RequestCheckpoint();
        Pump();
        return result;
    }

    /// <summary>The server loop's handling of writer events.</summary>
    public void Pump()
    {
        while (Writer.TryDequeueEvent(out var evt))
        {
            switch (evt)
            {
                case CheckpointCommitted committed:
                    State.CompleteCheckpoint(committed.Request.Core);
                    Blocks.OnCheckpointCommitted(committed.Request);
                    break;
                case CheckpointFailed failed:
                    State.RequeueAfterFailedCheckpoint(failed.Request.Core);
                    Blocks.OnCheckpointFailed(failed.Request);
                    break;
                case WriterFaulted faulted when !FaultsExpected:
                    ExceptionDispatchInfo.Throw(faulted.Error);
                    break;
                default:
                    break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Writer.DisposeAsync();
        Assert.True(await Blocks.CloseAsync());
        Store.Close();
    }

    private byte[]? ReadBlock(TerrainBlockKey key) =>
        Blocks.TryLoad(key, out _, out var blob) switch
        {
            TerrainBlockLookup.Found => blob,
            TerrainBlockLookup.NotEdited => null,
            _ => throw new InvalidOperationException($"Terrain block {key} is unavailable."),
        };
}
