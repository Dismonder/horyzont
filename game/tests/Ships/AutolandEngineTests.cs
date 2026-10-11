using System.Globalization;

using GdUnit4;

using Godot;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Simulation;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;
using Horyzont.Game.Frames;
using Horyzont.Game.Planet;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Server;
using Horyzont.Game.Server.Terrain;
using Horyzont.Game.Ships;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Terrain;
using Horyzont.Persistence;
using Horyzont.Server.Ships;
using Horyzont.Server.Space;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using static GdUnit4.Assertions;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Ships;

/// <summary>
/// K5-24 criterion 2 on the real terrain of the home planet with Jolt collisions: the 30-block ship hovers 100 m over the generated
/// ground, nobody takes the controls, the server flight (terrain gate over the K5-22 readiness, engine hull collider) autolands it and
/// parks it with at most 5 m of horizontal drift. One flight tick per physics frame.
/// </summary>
[TestSuite]
public class AutolandEngineTests
{
    private const ulong Seed = 21;
    private const int TickRate = 60;
    private const float StartHeight = 100f;
    private const int MaxTicks = 90 * TickRate;

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task Autoland_HomeTerrain_From100m_Parks()
    {
        await using var scene = EngineTestScene.Create();
        var time = new FakeTimeProvider();
        var created = StarSystemGenesis.CreateWorldBodies(Seed, GeneratorProfiles.Current);
        var state = WorldState.CreateNew(WorldFactory.DefaultWorldId, Seed, created.System, created.Bodies, ShipFixtures.BlockKeys, TerrainMaterials.Keys);
        var path = TerrainTestWorld.NewPath();
        WorldCreator.Create(path, state, TerrainTestWorld.Options, time);
        var graph = new FrameGraph(new WorldStateFramePoses(state, new WorldStatePlanetPlacement(state)));
        var nodes = scene.Add(new FrameNodes { Name = "Frames" });
        nodes.Initialize(graph.Source);
        var factory = new VoxelServerTerrainFactory(scene.Add(new Node3D { Name = "Terrain" }), PlanetTerrainOptions.Server, time, NullLoggerFactory.Instance);
        var terrain = (VoxelServerTerrain)factory.Create(path, state);
        terrain.Planets.FrameNodes = nodes;
        terrain.Planets.Graph = () => graph;
        var home = state.HomePlanet;
        state.Planets.TryGet(home, out var planet);
        try
        {
            using var lease = terrain.Planets.Acquire(home, PlanetLeaseReason.Dev);
            terrain.Planets.TryGetNode(home, out var node);
            var up = NumVector3.UnitY;
            terrain.Planets.TryGetGroundRadius(home, up, out var ground);

            var sim = new ServerSimulation(state, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance, ShipFixtures.ContentFor(state));
            var stats = new ShipStatsCache(sim);
            var ship = new EntityId(state.EntityIds.PeekNext().Value);
            var thirty = FlightFixtures.Thirty();
            var origin = up * (ground + StartHeight + thirty.BoundingRadius - thirty.CenterOfMass.Y);
            foreach (var op in ShipFixtures.BuildOperations(ship, ShipFixtures.Thirty, origin))
            {
                sim.ApplyServerOperation(op is ConstructCreateOp create ? create with { Parent = FrameRef.Of(home) } : op);
            }

            state.Constructs.TryGet(ship, out var construct);
            sim.ApplyServerOperation(new ShipModeSetOp(ship, ConstructMode.Ship, construct!.Parent, construct.ParentSeq, construct.Origin, construct.Rotation));
            sim.ApplyServerOperation(new ShipFuelAddOp(ship, 2000f, 2000f));

            using var collider = new GodotShipCollider(state, stats, frame => nodes.PlanetSpaceOf(new PlanetId(frame.Id), planet), static (_, _) => { });
            var gate = new PlanetTerrainShipGate(terrain.Planets, state, FlightRules.Default, PlanetGeneratorV1.MaxReliefMeters, TickRate, NullLogger.Instance);

            // Half a second of hovering with the gear up, then nobody at the controls.
            var hover = new ShipControl(0, 0, 0, 0, 0, 0, ShipButtons.Assist);
            var control = new ScriptedControlSource((_, n) => n < TickRate / 2 ? hover : null);
            var flight = new ShipFlightSystem(sim, stats, control, collider, gate, FlightRules.Default, TickRate, NullLogger.Instance);
            var viewers = new ServerShipViewers(scene.Add(new Node3D { Name = "ShipViewers" }));
            var tree = scene.Root.GetTree();
            var ticks = 0;
            var parked = false;
            while (ticks < MaxTicks && !parked)
            {
                if (ticks % ServerShipViewers.UpdateIntervalTicks == 0)
                {
                    state.Constructs.TryGet(ship, out var c);
                    var center = c!.LocalToParent(thirty.CenterOfMass);
                    var query = ShipTerrainQuery.Of(
                        center, thirty.BoundingRadius, NumVector3.Zero, MathF.Max(center.Length() - (planet!.RadiusMeters - PlanetGeneratorV1.MaxReliefMeters), 0f),
                        planet.RadiusMeters, PlanetGeneratorV1.MaxReliefMeters);
                    viewers.Apply(ship, home, query, node!, ground);
                }

                await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
                flight.Step();
                ticks++;
                parked = ticks > TickRate && state.ShipStates.TryGet(ship, out var record) && (record.Flags & ShipStateFlags.Parked) != 0;
            }

            state.Constructs.TryGet(ship, out var landed);
            var com = landed!.LocalToParent(thirty.CenterOfMass);
            var drift = (com - (up * com.Y)).Length();
            var height = com.Length() - ground - thirty.BoundingRadius;
            var text = string.Create(CultureInfo.InvariantCulture, $"K5-24 autoland on the home terrain from {StartHeight} m: parked={parked} after {ticks / (double)TickRate:F1} s, drift {drift:F2} m, centre {height:F2} m over the sphere contact");
            GD.Print(text);
            AssertThat(parked).OverrideFailureMessage(text).IsTrue();
            AssertThat(drift <= 5f).OverrideFailureMessage(text).IsTrue();
            viewers.Clear();
        }
        finally
        {
            await terrain.ShutdownAsync();
        }
    }
}
