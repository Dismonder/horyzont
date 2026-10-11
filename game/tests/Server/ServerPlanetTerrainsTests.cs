using GdUnit4;

using Godot;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Client;
using Horyzont.Game.Frames;
using Horyzont.Game.Planet;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Server;
using Horyzont.Game.Terrain;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Terrain;
using Horyzont.Persistence;
using Horyzont.Server.Space;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using static GdUnit4.Assertions;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Server;

/// <summary>K5-22 decisions 1, 3, 4, 6 on a server terrain of the four-body star system (engine).</summary>
[TestSuite]
public class ServerPlanetTerrainsTests
{
    private const ulong Seed = 21;

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerPlanetTerrains_Activation_AddsNodeUnderPlanetFrame()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        try
        {
            var planets = rig.Terrain.Planets;
            var moon = rig.Moon;
            AssertThat(planets.IsActive(rig.State.HomePlanet)).IsTrue();
            AssertThat(planets.IsActive(moon.Planet)).IsFalse();
            AssertThat(planets.GetReadiness(moon.Planet, new NumVector3(0, (float)moon.RadiusMeters, 0), 10f)).IsEqual(PlanetTerrainReadiness.Inactive);

            using (planets.Acquire(moon.Planet, PlanetLeaseReason.Dev))
            {
                AssertThat(planets.TryGetNode(moon.Planet, out var node)).IsTrue();
                AssertThat(rig.Nodes.TryGetNode(FrameRef.Of(moon.Planet), out var frameNode)).IsTrue();
                AssertThat(node!.GetParent() == frameNode).IsTrue();
                AssertThat(node.Transform == Transform3D.Identity).IsTrue();
                AssertThat(node.GlobalPosition.DistanceTo(GodotFrames.ToGodot(moon.Position)) < 1e-3).IsTrue();
                AssertThat(planets.GetReadiness(moon.Planet, new NumVector3(0, (float)moon.RadiusMeters, 0), 10f)).IsEqual(PlanetTerrainReadiness.Streaming);
            }
        }
        finally
        {
            await rig.DisposeAsync();
        }
    }

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerPlanetTerrains_Deactivation_RemovesNodeAfterLinger()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        try
        {
            var planets = rig.Terrain.Planets;
            var moon = rig.Moon.Planet;
            planets.Acquire(moon, PlanetLeaseReason.Dev).Dispose();
            AssertThat(planets.IsActive(moon)).IsTrue();

            rig.Ticks(3600 - 60);
            AssertThat(planets.IsActive(moon)).IsTrue();
            rig.Ticks(120);

            AssertThat(planets.IsActive(moon)).IsFalse();
            AssertThat(rig.Terrain.Voxel.TryGetPlanet(moon, out _)).IsFalse();
            AssertThat(planets.IsActive(rig.State.HomePlanet)).IsTrue();
            await scene.WaitFrames(2);

            // Edits live in the block buffer: the planet comes back with a new node.
            using var again = planets.Acquire(moon, PlanetLeaseReason.Dev);
            AssertThat(rig.Terrain.Voxel.TryGetPlanet(moon, out _)).IsTrue();
        }
        finally
        {
            await rig.DisposeAsync();
        }
    }

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerPlanetTerrains_DeactivationRefusedDuringReplayOrLease()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        try
        {
            var planets = rig.Terrain.Planets;
            var moon = rig.Moon.Planet;
            using (planets.Acquire(moon, PlanetLeaseReason.Dev))
            {
                rig.Ticks(4000);
                AssertThat(planets.IsActive(moon)).IsTrue();
            }

            using (planets.BeginReplay())
            {
                rig.Ticks(4000);
                AssertThat(planets.IsActive(moon)).IsTrue();
            }
        }
        finally
        {
            await rig.DisposeAsync();
        }
    }

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerPlanetTerrains_Readiness_InactiveStreamingReady()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        try
        {
            var planets = rig.Terrain.Planets;
            var moon = rig.Moon;
            var r = (float)moon.RadiusMeters;
            using var lease = planets.Acquire(moon.Planet, PlanetLeaseReason.Dev);

            // The check is made at the generated ground under the centre (LOD 0 exists only near a viewer).
            AssertThat(planets.TryGetGroundRadius(moon.Planet, NumVector3.UnitY, out var groundRadius)).IsTrue();
            var ground = new NumVector3(0, groundRadius, 0);

            // Above R + MaxRelief + 64 nothing is built: ready as soon as the node exists.
            var high = new NumVector3(0, r + PlanetGeneratorV1.MaxReliefMeters + 200f, 0);
            AssertThat(planets.GetReadiness(moon.Planet, high, 50f)).IsEqual(PlanetTerrainReadiness.Ready);
            AssertThat(planets.GetReadiness(moon.Planet, ground, 8f)).IsEqual(PlanetTerrainReadiness.Streaming);

            // A collision viewer at the ground: the data loads first, Ready only once the LOD 0 collision is applied.
            AssertThat(planets.TryGetNode(moon.Planet, out var node)).IsTrue();
            var viewer = new VoxelViewer { ViewDistance = 64, RequiresVisuals = false, RequiresCollisions = true };
            scene.Add(viewer);
            viewer.GlobalPosition = node!.GlobalPosition + TerrainVectors.ToGodot(ground);
            var sawLoadedNotReady = false;
            var ready = await scene.WaitFramesUntil(
                () =>
                {
                    var readiness = planets.GetReadiness(moon.Planet, ground, 8f);
                    sawLoadedNotReady |= readiness == PlanetTerrainReadiness.Streaming && rig.Terrain.Voxel.IsAreaLoaded(moon.Planet, ground, 8f);
                    return readiness == PlanetTerrainReadiness.Ready;
                },
                1200);
            AssertThat(ready).IsTrue();
            AssertThat(node.Terrain.IsAreaMeshed(new Aabb(TerrainVectors.ToGodot(ground) - new Godot.Vector3(8, 8, 8), new Godot.Vector3(16, 16, 16)), 0)).IsTrue();
            GD.Print($"K5-22 readiness: data loaded before collision observed = {sawLoadedNotReady}");
            AssertThat(sawLoadedNotReady).IsTrue();
            viewer.QueueFree();
        }
        finally
        {
            await rig.DisposeAsync();
        }
    }

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task PlanetTerrains_ActivateDeactivate_Repeated20_NoLeakAndEditsKept()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        try
        {
            var planets = rig.Terrain.Planets;
            var moon = rig.Moon.Planet;
            var nodesBefore = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
            var memoryBefore = Performance.GetMonitor(Performance.Monitor.MemoryStatic);
            for (var cycle = 0; cycle < 20; cycle++)
            {
                planets.Acquire(moon, PlanetLeaseReason.Dev).Dispose();
                AssertThat(planets.IsActive(moon)).IsTrue();
                await scene.WaitFrames(3);
                rig.Ticks(3700);
                AssertThat(planets.IsActive(moon)).IsFalse();
                await scene.WaitFrames(3);
            }

            AssertThat(rig.Terrain.Voxel.FirstFault is null).IsTrue();
            var nodesAfter = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
            var memoryAfter = Performance.GetMonitor(Performance.Monitor.MemoryStatic);
            GD.Print($"K5-22 repeated activation: nodes {nodesBefore} -> {nodesAfter}, static memory {memoryBefore} -> {memoryAfter}");
            AssertThat(Math.Abs(nodesAfter - nodesBefore) <= Math.Max(2, nodesBefore * 0.05)).IsTrue();
            AssertThat(memoryAfter <= memoryBefore * 1.05).IsTrue();
        }
        finally
        {
            await rig.DisposeAsync();
        }
    }

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task Readiness_StreamFault_Faulted()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        try
        {
            var planets = rig.Terrain.Planets;
            var moon = rig.Moon;
            using var lease = planets.Acquire(moon.Planet, PlanetLeaseReason.Dev);
            AssertThat(planets.TryGetNode(moon.Planet, out var node)).IsTrue();
            var high = new NumVector3(0, (float)moon.RadiusMeters + PlanetGeneratorV1.MaxReliefMeters + 500f, 0);
            AssertThat(planets.GetReadiness(moon.Planet, high, 10f)).IsEqual(PlanetTerrainReadiness.Ready);

            node!.Stream.ReportErrorForTest(new TerrainBlockKey(moon.Planet, 0, 0, 1, 0));

            // Reported at once, also where nothing is built, and never masked as Streaming.
            AssertThat(planets.GetReadiness(moon.Planet, high, 10f)).IsEqual(PlanetTerrainReadiness.Faulted);
            AssertThat(planets.GetReadiness(moon.Planet, new NumVector3(0, (float)moon.RadiusMeters, 0), 8f)).IsEqual(PlanetTerrainReadiness.Faulted);
        }
        finally
        {
            await rig.DisposeAsync();
        }
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task ServerShipViewers_ColumnUnderShip_ReachesGroundWithinLookahead()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        try
        {
            var planets = rig.Terrain.Planets;
            var moon = rig.Moon;
            var r = (float)moon.RadiusMeters;
            using var lease = planets.Acquire(moon.Planet, PlanetLeaseReason.Dev);
            AssertThat(planets.TryGetNode(moon.Planet, out var node)).IsTrue();
            var up = NumVector3.Normalize(new NumVector3(0.3f, 1f, 0.2f));
            AssertThat(planets.TryGetGroundRadius(moon.Planet, up, out var ground)).IsTrue();

            // A ship 50 m over the ground descending at 30 m/s (in a valley or over a hill: the column spans down to R − MaxRelief).
            var ship = up * (ground + 50f);
            var altitude = ship.Length() - (r - PlanetGeneratorV1.MaxReliefMeters);
            var query = ShipTerrainQuery.Of(ship, 10f, up * -30f, altitude, r, PlanetGeneratorV1.MaxReliefMeters);
            var column = query.Column;
            AssertThat(column.Center.Length() - column.Radius <= ground).IsTrue();
            AssertThat(column.Radius > TerrainSweep.FullBoxMaxRadius).IsTrue();
            AssertThat(planets.GetReadiness(moon.Planet, column.Center, column.Radius)).IsEqual(PlanetTerrainReadiness.Streaming);
            AssertThat(planets.GetShipReadiness(moon.Planet, query)).IsEqual(PlanetTerrainReadiness.Streaming);

            // Within 50 m of the ground the ground viewer covers the sweep box: one viewer.
            planets.ShipViewers.Apply(new EntityId(77), moon.Planet, query, node!, ground);
            AssertThat(planets.ShipViewers.TryGetViewer(new EntityId(77), out var viewer)).IsTrue();
            AssertThat(planets.ShipViewers.TryGetSweepViewer(new EntityId(77), out _)).IsFalse();
            var local = node!.ToLocal(viewer!.GlobalPosition);
            AssertThat(Math.Abs(local.Length() - ground) < 1.0).IsTrue();

            // The column-sized query and the ship query (column and sweep box) both become ready.
            var ready = await scene.WaitFramesUntil(() => planets.GetShipReadiness(moon.Planet, query) == PlanetTerrainReadiness.Ready, 1800);
            AssertThat(ready).IsTrue();
            AssertThat(planets.GetReadiness(moon.Planet, column.Center, column.Radius)).IsEqual(PlanetTerrainReadiness.Ready);

            // Deactivation takes the viewers of the planet with it.
            planets.ShipViewers.RemovePlanet(moon.Planet);
            AssertThat(planets.ShipViewers.Count).IsEqual(0);
        }
        finally
        {
            await rig.DisposeAsync();
        }
    }

    /// <summary>README Y11 (Opus review of K5-22): a ridge at hull height far from the ground viewer is not ready until the sweep viewer builds it.</summary>
    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task ShipReadiness_SidewaysAlongValleyWall_NotReadyUntilSweepViewerBuildsIt()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        try
        {
            var planets = rig.Terrain.Planets;
            var moon = rig.Moon;
            var r = (float)moon.RadiusMeters;
            using var lease = planets.Acquire(moon.Planet, PlanetLeaseReason.Dev);
            AssertThat(planets.TryGetNode(moon.Planet, out var node)).IsTrue();
            var (up, ground) = LowGround(planets, moon.Planet, r);

            // 350 m over the valley floor, flying sideways at 30 m/s: the sweep box is where a valley wall would meet the hull.
            var ship = up * (ground + 350f);
            var side = NumVector3.Normalize(NumVector3.Cross(up, NumVector3.UnitZ));
            var query = ShipTerrainQuery.Of(ship, 10f, side * 30f, ship.Length() - (r - PlanetGeneratorV1.MaxReliefMeters), r, PlanetGeneratorV1.MaxReliefMeters);
            AssertThat(query.HasSweep).IsTrue();

            // The ground viewer alone: the column (ground under the ship) gets ready, the ship query does not.
            planets.ShipViewers.Apply(new EntityId(78), moon.Planet, query.Column, node!, ground);
            var groundReady = await scene.WaitFramesUntil(
                () => planets.GetReadiness(moon.Planet, query.Column.Center, query.Column.Radius) == PlanetTerrainReadiness.Ready, 1800);
            AssertThat(groundReady).IsTrue();
            await scene.WaitFrames(120);
            AssertThat(planets.GetShipReadiness(moon.Planet, query)).IsEqual(PlanetTerrainReadiness.Streaming);

            // With the query, the second viewer stands at the sweep centre and builds the box.
            planets.ShipViewers.Apply(new EntityId(78), moon.Planet, query, node!, ground);
            AssertThat(planets.ShipViewers.TryGetSweepViewer(new EntityId(78), out var sweep)).IsTrue();
            AssertThat(node!.ToLocal(sweep!.GlobalPosition).DistanceTo(TerrainVectors.ToGodot(query.Sweep.Center)) < 1.0).IsTrue();
            var ready = await scene.WaitFramesUntil(() => planets.GetShipReadiness(moon.Planet, query) == PlanetTerrainReadiness.Ready, 1800);
            AssertThat(ready).IsTrue();
            var half = query.Sweep.HalfSize;
            var box = new Aabb(TerrainVectors.ToGodot(query.Sweep.Center) - new Godot.Vector3(half, half, half), new Godot.Vector3(2 * half, 2 * half, 2 * half));
            AssertThat(node.Terrain.IsAreaMeshed(box, 0)).IsTrue();
            planets.ShipViewers.RemovePlanet(moon.Planet);
            AssertThat(planets.ShipViewers.Count).IsEqual(0);
        }
        finally
        {
            await rig.DisposeAsync();
        }
    }

    /// <summary>README Y11: a ship down a player shaft, far under the generated ground, waits for the box around itself.</summary>
    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task ShipReadiness_DeepPlayerShaft_WaitsForTheBoxAroundTheShip()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        try
        {
            var planets = rig.Terrain.Planets;
            var moon = rig.Moon;
            var r = (float)moon.RadiusMeters;
            using var lease = planets.Acquire(moon.Planet, PlanetLeaseReason.Dev);
            AssertThat(planets.TryGetNode(moon.Planet, out var node)).IsTrue();
            var up = NumVector3.Normalize(new NumVector3(-0.4f, 1f, 0.3f));
            AssertThat(planets.TryGetGroundRadius(moon.Planet, up, out var ground)).IsTrue();

            // A viewer on the generated ground builds the surface, not the bottom of a 150 m shaft.
            var surface = new VoxelViewer { ViewDistance = 64, RequiresVisuals = false, RequiresCollisions = true };
            scene.Add(surface);
            surface.GlobalPosition = node!.GlobalPosition + TerrainVectors.ToGodot(up * ground);
            var ship = up * (ground - 150f);
            var query = ShipTerrainQuery.Of(ship, 6f, up * -4f, ship.Length() - (r - PlanetGeneratorV1.MaxReliefMeters), r, PlanetGeneratorV1.MaxReliefMeters);
            AssertThat(query.HasSweep).IsTrue();
            await scene.WaitFramesUntil(() => planets.GetReadiness(moon.Planet, up * ground, 8f) == PlanetTerrainReadiness.Ready, 1800);
            await scene.WaitFrames(60);
            AssertThat(planets.GetShipReadiness(moon.Planet, query)).IsEqual(PlanetTerrainReadiness.Streaming);

            planets.ShipViewers.Apply(new EntityId(79), moon.Planet, query, node, ground);
            AssertThat(planets.ShipViewers.TryGetSweepViewer(new EntityId(79), out var sweep)).IsTrue();
            AssertThat(node.ToLocal(sweep!.GlobalPosition).DistanceTo(TerrainVectors.ToGodot(ship)) <= 8.5).IsTrue();
            var ready = await scene.WaitFramesUntil(() => planets.GetShipReadiness(moon.Planet, query) == PlanetTerrainReadiness.Ready, 1800);
            AssertThat(ready).IsTrue();
            surface.QueueFree();
            planets.ShipViewers.RemovePlanet(moon.Planet);
        }
        finally
        {
            await rig.DisposeAsync();
        }
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Replay_EditsOnInactivePlanet_ActivatesWithLeaseAndReplays()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        try
        {
            var planets = rig.Terrain.Planets;
            var moon = rig.Moon.Planet;
            AssertThat(planets.IsActive(moon)).IsFalse();
            AssertThat(planets.TryGetGroundRadius(moon, NumVector3.UnitY, out var ground)).IsTrue();
            var edit = new TerrainEditOp(moon, TerrainShape.Sphere, new NumVector3(0, ground, 0), 2f, TerrainEditMode.Remove, 0);
            IReadOnlyList<JournalEntry> tail = [new JournalEntry(new OpId(1), rig.State.CurrentTick, null, edit)];
            AssertThat(TerrainReplay.EditedPlanets(tail).Contains(moon)).IsTrue();

            await rig.Terrain.ReplayAsync(rig.Sim, tail, CancellationToken.None);

            // The edit is in the state of the moon, the moon was activated for it and the replay lease is gone.
            AssertThat(rig.State.LastOpId).IsEqual(new OpId(1));
            AssertThat(rig.State.TerrainVersions.All.Any(version => version.Key.Planet == moon)).IsTrue();
            AssertThat(planets.IsActive(moon)).IsTrue();
            AssertThat(planets.Policy.LeaseCount(moon)).IsEqual(0);
            AssertThat(planets.IsReplaying).IsFalse();

            // Nobody demands it any more: off after the linger, and the edit survives the deactivation.
            rig.Ticks(3700);
            AssertThat(planets.IsActive(moon)).IsFalse();
            AssertThat(rig.State.TerrainVersions.All.Any(version => version.Key.Planet == moon)).IsTrue();
        }
        finally
        {
            await rig.DisposeAsync();
        }
    }

    /// <summary>Decision 10, host: one terrain instance lives while the server <b>or</b> the client wants it.</summary>
    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task SharedTerrain_HostKeepsNodeWhileServerOrClientWants()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        try
        {
            var planets = rig.Terrain.Planets;
            var moon = rig.Moon.Planet;
            var client = SharedTerrainProvider.Over(rig.Terrain);
            AssertThat(client.TryGetPlanet(moon, out _)).IsFalse();

            // The client alone holds the moon past the linger.
            AssertThat(client.Activate(moon)).IsTrue();
            AssertThat(client.Activate(moon)).IsTrue();
            AssertThat(client.TryGetPlanet(moon, out _)).IsTrue();
            rig.Ticks(3600 + 120);
            AssertThat(planets.IsActive(moon)).IsTrue();

            // The server wants it while the client lets go: the node stays.
            var server = planets.Acquire(moon, PlanetLeaseReason.Dev);
            client.Release(moon);
            rig.Ticks(3600 + 120);
            AssertThat(planets.IsActive(moon)).IsTrue();
            AssertThat(client.TryGetPlanet(moon, out _)).IsTrue();

            // Nobody wants it: it goes after the linger, for both.
            server.Dispose();
            rig.Ticks(3600 + 120);
            AssertThat(planets.IsActive(moon)).IsFalse();
            await scene.WaitFrames(2);
            AssertThat(client.TryGetPlanet(moon, out _)).IsFalse();
            AssertThat(client.Activate(rig.State.HomePlanet)).IsTrue();
            AssertThat(client.TryGetPlanet(rig.State.HomePlanet, out _)).IsTrue();
        }
        finally
        {
            await rig.DisposeAsync();
        }
    }

    /// <summary>A direction whose generated ground is below a radius (a valley), searched on a fixed spiral.</summary>
    private static (NumVector3 Up, float Ground) LowGround(Horyzont.Game.Server.Terrain.ServerPlanetTerrains planets, PlanetId planet, float below)
    {
        var best = (Up: NumVector3.UnitY, Ground: float.MaxValue);
        for (var i = 0; i < 256 && best.Ground >= below; i++)
        {
            var y = 1f - ((i + 0.5f) / 128f);
            var ring = MathF.Sqrt(MathF.Max(1f - (y * y), 0f));
            var angle = i * 2.39996323f;
            var up = new NumVector3(MathF.Cos(angle) * ring, y, MathF.Sin(angle) * ring);
            if (planets.TryGetGroundRadius(planet, up, out var ground) && ground < best.Ground)
            {
                best = (up, ground);
            }
        }

        AssertThat(best.Ground < below).IsTrue();
        return best;
    }

    private sealed class Rig : IAsyncDisposable
    {
        private Rig(WorldState state, VoxelServerTerrain terrain, FrameNodes nodes, BodyInfo moon)
        {
            State = state;
            Sim = new ServerSimulation(state, terrain.Service, new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
            Terrain = terrain;
            Nodes = nodes;
            Moon = moon;
        }

        public WorldState State { get; }

        public ServerSimulation Sim { get; }

        public VoxelServerTerrain Terrain { get; }

        public FrameNodes Nodes { get; }

        public BodyInfo Moon { get; }

        public static Rig Create(EngineTestScene scene)
        {
            var time = new FakeTimeProvider();
            var created = StarSystemGenesis.CreateWorldBodies(Seed, GeneratorProfiles.Current);
            var state = WorldState.CreateNew(WorldFactory.DefaultWorldId, Seed, created.System, created.Bodies, [WorldFactory.StoneBlockKey], TerrainMaterials.Keys);
            var path = TerrainTestWorld.NewPath();
            WorldCreator.Create(path, state, TerrainTestWorld.Options, time);
            var parent = scene.Add(new Node3D { Name = "Terrain" });
            var graph = new FrameGraph(new WorldStateFramePoses(state, new WorldStatePlanetPlacement(state)));
            var nodes = scene.Add(new FrameNodes { Name = "Frames" });
            nodes.Initialize(graph.Source);
            var factory = new VoxelServerTerrainFactory(parent, PlanetTerrainOptions.Server, time, NullLoggerFactory.Instance);
            var terrain = (VoxelServerTerrain)factory.Create(path, state);
            terrain.Planets.FrameNodes = nodes;
            terrain.Planets.Graph = () => graph;
            var model = new StarSystemModel(state.StarSystem!, state.Planets.All);
            return new Rig(state, terrain, nodes, model.GetBody(1));
        }

        public void Ticks(int count)
        {
            for (var i = 0; i < count; i++)
            {
                Terrain.Planets.Step(State.CurrentTick);
                Sim.AdvanceTick();
            }
        }

        public async ValueTask DisposeAsync() => await Terrain.ShutdownAsync();
    }
}
