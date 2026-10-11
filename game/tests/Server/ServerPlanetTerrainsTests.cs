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

            // A ship 50 m over the ground at 30 m/s (in a valley or over a hill: the column spans down to R − MaxRelief).
            var ship = up * (ground + 50f);
            var altitude = ship.Length() - (r - PlanetGeneratorV1.MaxReliefMeters);
            var column = TerrainColumn.UnderShip(ship, 10f, 30f, altitude, r, PlanetGeneratorV1.MaxReliefMeters);
            AssertThat(column.Center.Length() - column.Radius <= ground).IsTrue();
            AssertThat(planets.GetReadiness(moon.Planet, column.Center, column.Radius)).IsEqual(PlanetTerrainReadiness.Streaming);

            planets.ShipViewers.Apply(new EntityId(77), moon.Planet, column, node!, ground);
            AssertThat(planets.ShipViewers.TryGetViewer(new EntityId(77), out var viewer)).IsTrue();
            var local = node!.ToLocal(viewer!.GlobalPosition);
            AssertThat(Math.Abs(local.Length() - ground) < 1.0).IsTrue();

            var ready = await scene.WaitFramesUntil(() => planets.GetReadiness(moon.Planet, column.Center, column.Radius) == PlanetTerrainReadiness.Ready, 1800);
            AssertThat(ready).IsTrue();

            // Deactivation takes the viewers of the planet with it.
            planets.ShipViewers.RemovePlanet(moon.Planet);
            AssertThat(planets.ShipViewers.Count).IsEqual(0);
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
