using System.Numerics;
using System.Runtime.ExceptionServices;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Writing;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests.Support;

/// <summary>
/// A server in miniature: a loaded world, its replayed simulation, the writer and the event pump of the server loop.
/// The default world is <see cref="WorldFactory.CreateDefault"/>; a joined player stands on the north pole.
/// </summary>
internal sealed class WorldHarness : IAsyncDisposable
{
    public const string GameVersion = "test-1";

    private bool _disposed;

    private WorldHarness(string path, FakeTimeProvider time, LoadedWorld loaded, FakeTerrainService terrain, PersistenceWriter writer, ServerSimulation sim)
    {
        DbPath = path;
        Time = time;
        Loaded = loaded;
        Terrain = terrain;
        Writer = writer;
        Sim = sim;
    }

    public static PersistenceOptions Options => new() { GameVersion = GameVersion, BackupInterval = Timeout.InfiniteTimeSpan };

    public string DbPath { get; }

    public FakeTimeProvider Time { get; }

    public LoadedWorld Loaded { get; }

    public FakeTerrainService Terrain { get; }

    public PersistenceWriter Writer { get; }

    public ServerSimulation Sim { get; }

    public WorldState State => Sim.State;

    public List<PersistenceEvent> Events { get; } = [];

    public List<Exception> Faults { get; } = [];

    public int CheckpointsCommitted { get; private set; }

    public int CheckpointsFailed { get; private set; }

    /// <summary>Unexpected writer faults are rethrown by <see cref="Pump"/> (they also surface unimplemented codecs).</summary>
    public bool FaultsExpected { get; set; }

    public static WorldLoadOptions LoadOptions(string path) =>
        new() { BackupsDirectory = Path.Combine(Path.GetDirectoryName(path)!, "backups") };

    public static WorldHarness CreateNew(
        string path, IPersistenceFaults? faults = null, WorldState? world = null, PersistenceOptions? options = null, FakeTimeProvider? time = null)
    {
        time ??= new FakeTimeProvider();
        WorldCreator.Create(path, world ?? WorldFactory.CreateDefault(), options ?? Options, time);
        return Open(path, faults, options, time);
    }

    /// <summary>Loads, starts the writer, replays the tail (journal entries are already on disk, so replay does not journal).</summary>
    public static WorldHarness Open(
        string path, IPersistenceFaults? faults = null, PersistenceOptions? options = null, FakeTimeProvider? time = null, WorldLoadOptions? loadOptions = null)
    {
        time ??= new FakeTimeProvider();
        var loaded = WorldLoader.Load(path, loadOptions ?? LoadOptions(path), time, NullLogger.Instance);
        var terrain = NewTerrain(path);
        var writer = new PersistenceWriter(
            WorldDatabase.Open(path), options ?? Options, time, faults ?? NoFaults.Instance, NullLogger<PersistenceWriter>.Instance);
        var sim = new ServerSimulation(loaded.State, terrain, new JournalSink(writer), new GameRules(), NullLogger<ServerSimulation>.Instance);
        foreach (var entry in loaded.JournalTail)
        {
            sim.Replay(entry);
        }

        return new WorldHarness(path, time, loaded, terrain, writer, sim);
    }

    /// <summary>Loads and replays without a writer, as the game does at start; the replay journal is discarded.</summary>
    public static (LoadedWorld Loaded, ServerSimulation Sim, FakeTerrainService Terrain) LoadAndReplay(string path, WorldLoadOptions? options = null)
    {
        var loaded = WorldLoader.Load(path, options ?? LoadOptions(path), new FakeTimeProvider(), NullLogger.Instance);
        var terrain = NewTerrain(path);
        var sim = new ServerSimulation(loaded.State, terrain, new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        foreach (var entry in loaded.JournalTail)
        {
            sim.Replay(entry);
        }

        return (loaded, sim, terrain);
    }

    /// <summary>The oracle: the script from a fresh in-memory world up to <paramref name="target"/>.</summary>
    public static (ServerSimulation Sim, FakeTerrainService Terrain) Oracle(ulong seed, OpId target)
    {
        var terrain = new FakeTerrainService();
        var sim = new ServerSimulation(WorldFactory.CreateDefault(), terrain, new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        new WorldScript(seed).RunUntil(sim, target);
        return (sim, terrain);
    }

    /// <summary>Asserts equal block data for every terrain block either side knows a version of.</summary>
    public static void AssertSameTerrain(WorldState expectedState, FakeTerrainService expected, FakeTerrainService actual)
    {
        foreach (var version in expectedState.TerrainVersions.All)
        {
            Assert.Equal(expected.GetBlockData(version.Key), actual.GetBlockData(version.Key));
        }

        foreach (var key in actual.Blocks.Keys)
        {
            Assert.Equal(expected.GetBlockData(key), actual.GetBlockData(key));
        }
    }

    private static FakeTerrainService NewTerrain(string path)
    {
        var store = new TerrainBlockStore(path);
        return new FakeTerrainService { BlockSource = key => store.TryLoad(key, out _, out var data) ? data : null };
    }

    /// <summary>The server loop's handling of writer events.</summary>
    public void Pump()
    {
        while (Writer.TryDequeueEvent(out var evt))
        {
            Events.Add(evt);
            switch (evt)
            {
                case CheckpointCommitted committed:
                    State.CompleteCheckpoint(committed.Request.Core);
                    CheckpointsCommitted++;
                    break;
                case CheckpointFailed failed:
                    State.RequeueAfterFailedCheckpoint(failed.Request.Core);
                    CheckpointsFailed++;
                    break;
                case WriterFaulted faulted:
                    Faults.Add(faulted.Error);
                    if (!FaultsExpected)
                    {
                        ExceptionDispatchInfo.Throw(faulted.Error);
                    }

                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>Captures a checkpoint with the fake terrain's block data and hands it to the writer.</summary>
    public Task<bool> RequestCheckpoint()
    {
        var core = State.CaptureCheckpoint();
        var blocks = new List<TerrainBlockPayload>(core.TerrainVersions.Count);
        foreach (var version in core.TerrainVersions)
        {
            blocks.Add(new TerrainBlockPayload(version.Key, version.Version, Terrain.GetBlockData(version.Key)));
        }

        return Writer.CheckpointAsync(new CheckpointRequest(core, blocks));
    }

    /// <summary>Checkpoint, wait for it and apply its event.</summary>
    public async Task<bool> CheckpointAndPump()
    {
        var result = await RequestCheckpoint();
        Pump();
        return result;
    }

    public async Task FlushAndPump()
    {
        Assert.True(await Writer.FlushAsync(), "Flush failed.");
        Pump();
    }

    public PlayerId Join(ulong identity, string name = "player")
    {
        var op = Sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, identity), name);
        Sim.ApplyServerOperation(op);
        return op.Player;
    }

    public void Leave(PlayerId id)
    {
        State.Players.TryGet(id, out var p);
        Sim.ApplyServerOperation(new PlayerLeaveOp(id, p!.Frame, p.ParentSeq, p.Position, p.BodyRotation, p.Pitch));
    }

    public Vector3 Eye(PlayerId id)
    {
        State.Players.TryGet(id, out var p);
        return p!.EyePosition(Sim.Rules);
    }

    public Vector3 Ground(float x, float z = 0)
    {
        var r = Terrain.SurfaceRadius;
        return new(x, MathF.Sqrt((r * r) - (x * x) - (z * z)), z);
    }

    public ushort Stone
    {
        get
        {
            State.BlockTypes.TryGetId(WorldFactory.StoneBlockKey, out var stone);
            return stone;
        }
    }

    public EntityId BuildOnTerrain(PlayerId player, float x, float z = 0)
    {
        var eye = Eye(player);
        var result = Sim.Submit(
            player, new PlaceBlockIntent(new TerrainTarget(WorldFactory.HomePlanet, eye, Vector3.Normalize(Ground(x, z) - eye), -Vector3.UnitZ), Stone, 0));
        Assert.True(result.IsAccepted, $"Place rejected: {result.Reason}");
        return ((ConstructCreateOp)result.Applied!.Operation).Construct;
    }

    public void Dig(PlayerId player, float x, float z = 0)
    {
        var eye = Eye(player);
        var result = Sim.Submit(
            player, new TerrainToolIntent(WorldFactory.HomePlanet, eye, Vector3.Normalize(Ground(x, z) - eye), 1f, TerrainEditMode.Remove, 0));
        Assert.True(result.IsAccepted, $"Dig rejected: {result.Reason}");
    }

    public void Remove(PlayerId player, EntityId construct, CellPos cell)
    {
        var result = Sim.Submit(player, new RemoveBlockIntent(construct, cell));
        Assert.True(result.IsAccepted, $"Remove rejected: {result.Reason}");
    }

    public void Ticks(int count)
    {
        for (var i = 0; i < count; i++)
        {
            Sim.AdvanceTick();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await Writer.DisposeAsync();
    }
}
