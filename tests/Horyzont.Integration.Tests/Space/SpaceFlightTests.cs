using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Integration.Tests.Support;
using Horyzont.Persistence;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Writing;
using Horyzont.Server.Frames;
using Horyzont.Server.Space;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Integration.Tests.Space;

/// <summary>
/// K5-21 step 9: a ship flies in a straight line from the home planet to its moon through the system frame. The zone transitions
/// reparent it Planet(home) → System → Planet(moon) without a jump of the world pose, and the journal replays (from scratch and from
/// a checkpoint on disk) to the same state.
/// </summary>
[Collection(SqlitePools.Name)]
public sealed class SpaceFlightTests : IDisposable
{
    private const ulong Seed = 21;
    private const double MaxJumpMeters = 1e-3;
    private const double StepMeters = 10;
    private static readonly EntityId _ship = new(10);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "horyzont-tests", Guid.NewGuid().ToString("N"));

    public SpaceFlightTests() => Directory.CreateDirectory(_dir);

    private string WorldPath => Path.Combine(_dir, "world.db");

    public void Dispose()
    {
        // Pooled read connections keep the file open on Windows until this world's pool is closed.
        new TerrainBlockStore(WorldPath).Close();
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
    public async Task SpaceFlight_HomeToMoon_ThreeFramesContinuousAndReplayable()
    {
        var created = StarSystemGenesis.CreateWorldBodies(Seed, WorldFactory.HomePlanetParams.GeneratorProfile);
        var model = new StarSystemModel(created.System, created.Bodies);
        var home = model.GetBody(0);
        var moon = model.GetBody(1);
        var direction = Vector3D.Normalize(moon.Position - home.Position);
        var start = home.Position + (direction * (home.RadiusMeters + 20));
        var end = moon.Position - (direction * (moon.RadiusMeters + 20));

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));
        var options = new PersistenceOptions { GameVersion = "k5-21", BackupInterval = Timeout.InfiniteTimeSpan };

        // A new world holds no constructs, so the ship enters the file with the first checkpoint (one block edit marks its chunk).
        WorldCreator.Create(WorldPath, TestShips.CreateStarSystemWorld(Seed), options, time);
        var loadOptions = new WorldLoadOptions { BackupsDirectory = Path.Combine(_dir, "backups") };
        Assert.Equal(created.System, WorldLoader.Load(WorldPath, loadOptions, time, NullLogger.Instance).State.StarSystem);
        await using var writer = new PersistenceWriter(
            WorldDatabase.Open(WorldPath), options, time, new ArmedFaults(), NullLogger<PersistenceWriter>.Instance);
        var all = new List<JournalEntry>();
        var sim = new ServerSimulation(
            InitialWorld(start - home.Position, home.Planet), new FakeTerrainService(), new TeeSink(new JournalSink(writer), all), new GameRules(),
            NullLogger<ServerSimulation>.Instance);
        sim.ApplyServerOperation(new BlockSetOp(_ship, new CellPos(0, 1, 0), BlockState.Create(TestShips.StoneTypeId, 0)));
        await CheckpointAsync(sim, writer);
        var graph = new FrameGraph(new WorldStateFramePoses(sim.State, new WorldStatePlanetPlacement(sim.State)));
        var history = new ServerFrameHistory(sim.State, graph);
        var transitions = new FrameTransitions(sim, history);
        var zones = new ServerZoneTransitions(() => transitions, sim.Rules.TickRate, NullLogger<ServerZoneTransitions>.Instance);

        var parents = new List<FrameRef> { Ship(sim).Parent };
        var checkpointed = false;
        var total = Vector3D.Distance(start, end);
        for (var travelled = 0.0; travelled <= total && zones.Reparented < 2; travelled += StepMeters)
        {
            var world = start + (direction * travelled);
            var ship = Ship(sim);
            var parentPosition = ship.Parent.Kind == FrameKind.Planet ? model.GetBody(BodyIndex(model, ship.Parent)).Position : default;
            Assert.True(sim.UpdateConstructKinematics(_ship, ship.Parent, ship.ParentSeq, ToFloat(world - parentPosition), Quaternion.Identity));

            Assert.True(graph.TryGetWorldPose(FrameRef.Of(_ship), out var before));
            zones.Step(sim.State);
            Assert.True(graph.TryGetWorldPose(FrameRef.Of(_ship), out var after));
            Assert.InRange(Vector3D.Distance(before.Position, after.Position), 0, MaxJumpMeters);
            if (Ship(sim).Parent != parents[^1])
            {
                parents.Add(Ship(sim).Parent);
            }

            // A checkpoint while the ship crosses the system frame: the restart replays the second reparent from the tail.
            if (!checkpointed && zones.Reparented == 1)
            {
                await CheckpointAsync(sim, writer);
                checkpointed = true;
            }

            history.Step(sim.State.CurrentTick);
            sim.AdvanceTick();
        }

        // The flight stops at the moon border: the last journalled pose is the live one (kinematics are not journalled).
        Assert.Equal([FrameRef.Of(home.Planet), FrameRef.Of(created.System.System), FrameRef.Of(moon.Planet)], parents);
        Assert.Equal(0, zones.Refused);
        var reparents = all.Select(e => e.Operation).OfType<EntityReparentOp>().ToList();
        Assert.Equal(2, reparents.Count);
        Assert.All(reparents, op => Assert.Equal(_ship, op.Entity));

        Assert.True(await writer.FlushAsync());
        Drain(writer, sim.State);
        var liveHash = StateHasher.Hash(sim.State);

        var replayed = new ServerSimulation(
            InitialWorld(start - home.Position, home.Planet), new FakeTerrainService(), new InMemoryJournal(), new GameRules(),
            NullLogger<ServerSimulation>.Instance);
        foreach (var entry in all)
        {
            replayed.Replay(entry);
        }

        Assert.Equal(sim.State.LastOpId, replayed.State.LastOpId);
        Assert.Equal(liveHash, StateHasher.Hash(replayed.State));

        var recovered = WorldLoader.Load(WorldPath, loadOptions, new FakeTimeProvider(), NullLogger.Instance);
        Assert.True(recovered.LastCheckpointOpId > OpId.None, "recovery must start from the checkpoint");
        Assert.Single(recovered.JournalTail);
        var fresh = new ServerSimulation(
            recovered.State, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        foreach (var entry in recovered.JournalTail)
        {
            fresh.Replay(entry);
        }

        Assert.Equal(created.System, fresh.State.StarSystem);
        Assert.Equal(liveHash, StateHasher.Hash(fresh.State));
    }

    private static WorldState InitialWorld(Vector3D local, PlanetId home) =>
        TestShips.CreateStarSystemWorld(Seed, new TestShip(_ship, FrameRef.Of(home), ToFloat(local), Quaternion.Identity));

    private static Construct Ship(ServerSimulation sim) =>
        sim.State.Constructs.TryGet(_ship, out var ship) ? ship : throw new InvalidOperationException();

    private static int BodyIndex(StarSystemModel model, FrameRef planet)
    {
        for (var i = 0; ; i++)
        {
            if (FrameRef.Of(model.GetBody(i).Planet) == planet)
            {
                return i;
            }
        }
    }

    private static async Task CheckpointAsync(ServerSimulation sim, PersistenceWriter writer)
    {
        Assert.True(await writer.FlushAsync());
        Drain(writer, sim.State);
        Assert.True(await writer.CheckpointAsync(new CheckpointRequest(sim.State.CaptureCheckpoint(), [])));
        Drain(writer, sim.State);
    }

    private static void Drain(PersistenceWriter writer, WorldState state)
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

    private static Vector3 ToFloat(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

    private sealed class TeeSink(IJournalSink inner, List<JournalEntry> copy) : IJournalSink
    {
        public void Append(JournalEntry entry)
        {
            copy.Add(entry);
            inner.Append(entry);
        }
    }
}
