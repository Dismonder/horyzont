using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Integration.Tests.Support;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Persistence;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Writing;
using Horyzont.Testing;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Integration.Tests;

/// <summary>
/// The K1 gate: a server (simulation, persistence writer on a SQLite file) and two clients talk through the in-memory
/// transport; after a simulated process death the world loaded from disk plus the journal tail hashes like the live one.
/// </summary>
public sealed class K1GateTests : IDisposable
{
    private const ulong Build = 0x4B31_6174_6500_0001UL;
    private static readonly TimeSpan _step = TimeSpan.FromMilliseconds(10);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "horyzont-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
    private ulong _tick;

    public K1GateTests() => Directory.CreateDirectory(_dir);

    private string WorldPath => Path.Combine(_dir, "world.db");

    private WorldLoadOptions LoadOptions => new() { BackupsDirectory = Path.Combine(_dir, "backups") };

    public void Dispose()
    {
        // Pooled read connections keep the file open on Windows until the pools are cleared.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Safe to leave: the directory is unique and under the temp directory.
        }
    }

    [Fact]
    public async Task TwoClients_SubmitIntents_CrashAfterJournalCommit_LoadAndReplayMatchesLiveHash()
    {
        var options = new PersistenceOptions { GameVersion = "k1-gate", BackupInterval = Timeout.InfiniteTimeSpan };
        WorldCreator.Create(WorldPath, WorldFactory.CreateDefault(), options, _time);
        var loaded = WorldLoader.Load(WorldPath, LoadOptions, _time, NullLogger.Instance);
        var terrain = new FakeTerrainService();
        var faults = new ArmedFaults();
        await using var writer = new PersistenceWriter(WorldDatabase.Open(WorldPath), options, _time, faults, NullLogger<PersistenceWriter>.Instance);
        var sink = new HashingJournalSink(loaded.State, new JournalSink(writer));
        var sim = new ServerSimulation(loaded.State, terrain, sink, new GameRules(), NullLogger<ServerSimulation>.Instance);
        var hooks = new SimulationServerHooks(sim);

        var network = new InMemoryNetwork(_time);
        using var serverTransport = network.CreateTransport("server", TransportOptions.Default);
        serverTransport.Listen(new InMemoryEndpoint("server"));
        var server = new ServerSession(serverTransport, new ServerOptions { BuildHash = Build }, hooks, _time);

        using var transportA = network.CreateTransport("alice", TransportOptions.Default);
        using var transportB = network.CreateTransport("bob", TransportOptions.Default);
        var hooksA = new RecordingClientHooks();
        var hooksB = new RecordingClientHooks();
        var alice = new ClientSession(transportA, new ClientOptions { BuildHash = Build }, hooksA, _time);
        var bob = new ClientSession(transportB, new ClientOptions { BuildHash = Build }, hooksB, _time);
        var idA = new PlatformIdentity(IdentityPlatform.Dev, 1001);
        var idB = new PlatformIdentity(IdentityPlatform.Dev, 1002);
        alice.Connect(new InMemoryEndpoint("server"), idA, "alice");
        bob.Connect(new InMemoryEndpoint("server"), idB, "bob");

        void Pump()
        {
            _time.Advance(_step);
            alice.Poll();
            bob.Poll();
            server.Poll(new Tick(++_tick));
            sim.AdvanceTick();
            DrainWriterEvents(writer, sim.State);
        }

        bool RunUntil(Func<bool> condition)
        {
            for (var i = 0; i < 1000 && !condition(); i++)
            {
                Pump();
            }

            return condition();
        }

        Assert.True(RunUntil(() => alice.State == ClientState.Loading && bob.State == ClientState.Loading), "clients did not reach Loading");
        alice.SendReady();
        bob.SendReady();
        Assert.True(RunUntil(() => alice.State == ClientState.InGame && bob.State == ClientState.InGame), "clients did not reach InGame");
        var playerA = hooks.PlayerOf(idA);
        var playerB = hooks.PlayerOf(idB);
        Assert.NotEqual(playerA, playerB);
        Assert.Equal(playerA, hooksA.Hello!.YourPlayer);

        var expectedReplies = 0;
        void SubmitRound(float xa, float za, float xb, float zb)
        {
            alice.SubmitIntent(PlaceStone(sim, terrain, playerA, xa, za));
            bob.SubmitIntent(PlaceStone(sim, terrain, playerB, xb, zb));
            expectedReplies++;
            Assert.True(RunUntil(() => hooksA.Replies.Count == expectedReplies && hooksB.Replies.Count == expectedReplies), "intent replies missing");
        }

        // Phase 1: valid intents from both clients, then flush and a checkpoint (recovery uses checkpoint + tail).
        SubmitRound(-3f, 0f, 3f, 0f);
        SubmitRound(0f, -3f, 0f, 3f);
        Assert.True(await writer.FlushAsync());
        DrainWriterEvents(writer, sim.State);

        var core = sim.State.CaptureCheckpoint();
        var blocks = core.TerrainVersions.Select(v => new TerrainBlockPayload(v.Key, v.Version, terrain.GetBlockData(v.Key))).ToList();
        Assert.True(await writer.CheckpointAsync(new CheckpointRequest(core, blocks)));
        DrainWriterEvents(writer, sim.State);
        Assert.False(sim.State.IsCheckpointInFlight);

        // Phase 2: more intents after the checkpoint; the flush makes them durable.
        SubmitRound(-3f, -3f, 3f, 3f);
        Assert.True(await writer.FlushAsync());
        var durableBeforeCrash = writer.DurableOpId;
        Assert.True(durableBeforeCrash >= sim.State.LastOpId);
        Assert.All(hooks.Results, r => Assert.True(r.Result.IsAccepted, $"rejected: {r.Result.Reason}"));
        Assert.All(hooksA.Replies.Concat(hooksB.Replies), r => Assert.Equal(RejectReason.None, r.Reason));
        Assert.Equal(6, hooks.Results.Count);

        // Phase 3: the process dies right after the next journal commit.
        faults.Arm(FaultPoint.AfterJournalCommit);
        SubmitRound(-3f, 3f, 3f, -3f);
        Assert.False(await writer.FlushAsync());
        Assert.True(faults.Crashed);
        Assert.True(writer.IsFaulted);
        var liveLast = sim.State.LastOpId;
        Assert.Equal(StateHasher.Hash(sim.State), sink.HashAt[liveLast]);

        // Recovery: load the file, replay the tail into a fresh simulation.
        var recovered = WorldLoader.Load(WorldPath, LoadOptions, new FakeTimeProvider(), NullLogger.Instance);
        Assert.True(recovered.LastCheckpointOpId > OpId.None, "recovery must start from the checkpoint");
        Assert.NotEmpty(recovered.JournalTail);
        var store = new TerrainBlockStore(WorldPath);
        var recoveredTerrain = new FakeTerrainService { BlockSource = key => store.TryLoad(key, out _, out var data) ? data : null };
        var fresh = new ServerSimulation(recovered.State, recoveredTerrain, new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        foreach (var entry in recovered.JournalTail)
        {
            fresh.Replay(entry);
        }

        Assert.True(fresh.State.LastOpId >= durableBeforeCrash, $"lost durable operations: {fresh.State.LastOpId.Value} < {durableBeforeCrash.Value}");
        Assert.True(fresh.State.LastOpId <= liveLast);
        Assert.Equal(sink.HashAt[fresh.State.LastOpId], StateHasher.Hash(fresh.State));
    }

    private static PlaceBlockIntent PlaceStone(ServerSimulation sim, FakeTerrainService terrain, PlayerId player, float x, float z)
    {
        sim.State.Players.TryGet(player, out var p);
        var eye = p!.EyePosition(sim.Rules);
        var r = terrain.SurfaceRadius;
        var ground = new Vector3(x, MathF.Sqrt((r * r) - (x * x) - (z * z)), z);
        sim.State.BlockTypes.TryGetId(WorldFactory.StoneBlockKey, out var stone);
        return new PlaceBlockIntent(new TerrainTarget(WorldFactory.HomePlanet, eye, Vector3.Normalize(ground - eye), -Vector3.UnitZ), stone, 0);
    }

    private static void DrainWriterEvents(PersistenceWriter writer, WorldState state)
    {
        while (writer.TryDequeueEvent(out var evt))
        {
            switch (evt)
            {
                case CheckpointCommitted committed:
                    state.CompleteCheckpoint(committed.Request.Core);
                    break;
                case CheckpointFailed failed:
                    state.RequeueAfterFailedCheckpoint(failed.Request.Core);
                    break;
                default:
                    break;
            }
        }
    }
}
