using System.Globalization;

using GdUnit4;

using Godot;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Game.Frames;
using Horyzont.Game.Planet;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Server;
using Horyzont.Game.Server.Terrain;
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

namespace Horyzont.Game.Tests.Space;

/// <summary>
/// K5-22 criterion 1 (spec 02:220, :247): a probe with the viewer of a ship (decision 5, column of README Y11) descends from 8 km over
/// the moon and over planet C (about 108 km from the origin) with <c>v = clamp(h/τ, 100, 2000)</c>, τ = 8 s, one physics tick per
/// simulated tick; from <c>h ≤ 50 m</c> every tick a physics ray down from the probe must hit the terrain collision within 1 m of the
/// generated surface, and touching down (<c>h ≤ 2 m</c>) without collision fails the trial. Trials: 3 in the job <c>game</c>
/// (<c>HORYZONT_DESCENT_TRIALS</c> = 20 in the nightly benchmark); half on the moon, half on C, directions from the trial seed.
/// </summary>
[TestSuite]
public class DescentTests
{
    private const ulong Seed = 21;
    private const double Tau = 8.0;
    private const double TickSeconds = 1.0 / 60.0;
    private const float StartAltitude = 8000f;
    private const float CheckBelow = 50f;
    private const float Touchdown = 2f;
    private const float ShipRadius = 6f;
    private const int MaxWaitTicks = 20 * 60;

    [TestCase(Timeout = 3600000)]
    [TestCategory("Slow")]
    [RequireGodotRuntime]
    public async Task Descent8km_TwentyTrials_NoCollisionGapsAtTouchdown()
    {
        var trials = int.TryParse(System.Environment.GetEnvironmentVariable("HORYZONT_DESCENT_TRIALS"), out var n) && n > 0 ? n : 3;
        await using var scene = EngineTestScene.Create();
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
        var moon = model.GetBody(1);
        var planetC = Enumerable.Range(2, model.BodyCount - 2).Select(model.GetBody).MaxBy(b => b.Position.Length());
        var viewers = new ServerShipViewers(scene.Add(new Node3D { Name = "ShipViewers" }));
        var report = new List<string>();
        var failures = 0;
        try
        {
            for (var trial = 0; trial < trials; trial++)
            {
                var body = trial % 2 == 0 ? moon : planetC;
                var result = await Descend(scene, terrain, viewers, body, new Random(1000 + trial));
                report.Add(string.Create(CultureInfo.InvariantCulture,
                    $"trial {trial} planet {body.Planet.Value}: {(result.Ok ? "ok" : "FAIL")} descent {result.Seconds:F1} s, worst wait {result.WorstWaitTicks} ticks, {result.Reason}"));
                failures += result.Ok ? 0 : 1;
                viewers.Clear();
            }
        }
        finally
        {
            await terrain.ShutdownAsync();
        }

        var text = string.Create(CultureInfo.InvariantCulture, $"K5-22 descent 8 km: {trials - failures}/{trials} ok") + "\n" + string.Join('\n', report);
        GD.Print(text);
        if (System.Environment.GetEnvironmentVariable("HORYZONT_DESCENT_REPORT") is { Length: > 0 } file)
        {
            await File.WriteAllTextAsync(file, text);
        }

        AssertThat(failures).OverrideFailureMessage(text).IsEqual(0);
    }

    private static async Task<Result> Descend(EngineTestScene scene, VoxelServerTerrain terrain, ServerShipViewers viewers, BodyInfo body, Random random)
    {
        var planets = terrain.Planets;
        using var lease = planets.Acquire(body.Planet, PlanetLeaseReason.Dev);
        if (!planets.TryGetNode(body.Planet, out var node))
        {
            return new Result(false, 0, 0, "no terrain node");
        }

        var direction = RandomDirection(random);
        planets.TryGetGroundRadius(body.Planet, direction, out var ground);
        var radius = (float)body.RadiusMeters;
        var lowest = radius - PlanetGeneratorV1.MaxReliefMeters;
        var h = (double)StartAltitude;
        var tree = scene.Root.GetTree();
        var ticks = 0;
        var waiting = 0;
        var worstWait = 0;
        var mask = (uint)node!.Terrain.CollisionLayer;
        while (true)
        {
            var speed = Math.Clamp(h / Tau, 100.0, 2000.0);
            var centre = direction * (ground + (float)h);
            if (ticks % ServerShipViewers.UpdateIntervalTicks == 0)
            {
                var column = TerrainColumn.UnderShip(centre, ShipRadius, (float)speed, MathF.Max(centre.Length() - lowest, 0f), radius, PlanetGeneratorV1.MaxReliefMeters);
                viewers.Apply(new EntityId(9000), body.Planet, column, node, ground);
            }

            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            ticks++;
            if (h <= CheckBelow)
            {
                if (Hits(node, centre, direction, ground, mask))
                {
                    waiting = 0;
                }
                else
                {
                    waiting++;
                    worstWait = Math.Max(worstWait, waiting);
                    if (h <= Touchdown)
                    {
                        return new Result(false, ticks * TickSeconds, worstWait, string.Create(CultureInfo.InvariantCulture, $"no collision at touchdown, h {h:F1} m"));
                    }

                    // Without collision the ship hovers (K5-24 CollisionPending); the gap is the waiting time, at most the readiness
                    // timeout of K5-24 (20 s).
                    if (waiting > MaxWaitTicks)
                    {
                        return new Result(false, ticks * TickSeconds, worstWait, string.Create(CultureInfo.InvariantCulture, $"no collision for 20 s at h {h:F1} m"));
                    }

                    continue;
                }
            }

            if (h <= Touchdown)
            {
                return new Result(true, ticks * TickSeconds, worstWait, "touchdown");
            }

            h = Math.Max(h - (speed * TickSeconds), Touchdown);
        }
    }

    private static bool Hits(PlanetTerrain node, NumVector3 centre, NumVector3 direction, float ground, uint mask)
    {
        var space = PhysicsServer3D.SpaceGetDirectState(node.GetWorld3D().Space);
        var from = node.GlobalTransform * TerrainVectors.ToGodot(centre);
        var to = node.GlobalTransform * TerrainVectors.ToGodot(direction * (ground - 10f));
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, mask));
        if (hit.Count == 0)
        {
            return false;
        }

        var local = node.GlobalTransform.AffineInverse() * (Vector3)hit["position"];
        return Math.Abs(local.Length() - ground) <= 1.0;
    }

    private static NumVector3 RandomDirection(Random random)
    {
        while (true)
        {
            var v = new NumVector3((float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1));
            var length = v.Length();
            if (length is > 0.1f and <= 1f)
            {
                return v / length;
            }
        }
    }

    private readonly record struct Result(bool Ok, double Seconds, int WorstWaitTicks, string Reason);
}
