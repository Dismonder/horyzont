using System.Globalization;

using GdUnit4;

using Godot;

using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Constructs;
using Horyzont.Game.Physics;
using Horyzont.Game.Planet;
using Horyzont.Game.Server.Characters;
using Horyzont.Game.Tests.Character.Fixtures;
using Horyzont.Game.Tests.Constructs;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Terrain;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Frames;

/// <summary>
/// K5-20 step 1 (criterion 1): Jolt in double precision stays stable about 115 km from the origin of the system frame, so the
/// Godot world can be the system frame without a floating origin (decision 1). A failure stops the package (P1).
/// </summary>
[TestSuite]
public class FarFromOriginTests
{
    private const double JitterLimitMeters = 1e-3;
    private const int SettleTicks = 120;
    private const int MeasureTicks = 600;
    private const double Tick = 1.0 / 60.0;

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task FarFromOrigin_CharacterOnBox115km_JitterBelow1mm()
    {
        await using var scene = EngineTestScene.Create();

        // Radial gravity towards the origin: the box at (115 km, 0, 0) faces +X.
        var top = new Vector3(115_000, 0, 0);
        var box = new StaticBody3D { Name = "Box", CollisionLayer = CollisionLayers.TerrainMask, CollisionMask = 0 };
        box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1, 20, 20) } });
        scene.Add(box);
        box.GlobalPosition = top - new Vector3(0.5, 0, 0);
        var body = FarCharacter.Create(new GameRules(), Vector3.Zero);
        scene.Add(body);
        body.Place(top + new Vector3(0.05, 0, 0));

        var result = await MeasureStandingAsync(scene, body);

        Record($"box115km max_jitter_m={F(result.MaxJitter)} floor_lost={result.FloorLost} height_m={F(body.GlobalPosition.X - top.X)}");
        AssertThat(result.Settled).IsTrue();
        AssertThat(result.MaxJitter).IsLessEqual(JitterLimitMeters);
        AssertThat(result.FloorLost).IsEqual(0);
        AssertThat(Math.Abs(body.GlobalPosition.X - top.X)).IsLess(0.05);
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task FarFromOrigin_VoxelTerrainAt110km_CharacterStableAndColliding()
    {
        await using var scene = EngineTestScene.Create();
        var planet = TerrainTestWorld.Planet;

        // A test instance of the home planet terrain (generator v1, no stream) moved 110 km along +X.
        var centre = new Vector3(110_000, 0, 0);
        var terrain = PlanetTerrainFactory.Create(planet, PlanetTerrainOptions.Server);
        terrain.Position = centre;
        scene.Add(terrain);
        var spawn = SpawnLocator.ForNewPlayer(planet).Feet;
        var feet = centre + new Vector3(spawn.X, spawn.Y, spawn.Z);
        var body = FarCharacter.Create(new GameRules(), centre);
        scene.Add(body);
        body.Place(feet + ((feet - centre).Normalized() * 0.5));
        body.AddChild(new VoxelViewer { ViewDistance = 64, RequiresVisuals = false, RequiresCollisions = true });

        // Frozen until the collision below the spawn exists.
        var stepper = scene.Add(new PhysicsStepper());
        var groundFound = false;
        stepper.OnTick = _ =>
        {
            if (!groundFound && TryRayDown(body, 4, out var hit))
            {
                groundFound = true;
                body.Place(hit + (body.Up * 0.05));
            }
        };
        AssertThat(await scene.WaitFramesUntil(() => groundFound, 20000)).IsTrue();
        AssertThat(body.GlobalPosition.DistanceTo(Vector3.Zero)).IsGreater(113_000.0);

        var result = await MeasureStandingAsync(scene, body, stepper);
        AssertThat(result.Settled).IsTrue();
        AssertThat(result.MaxJitter).IsLessEqual(JitterLimitMeters);
        AssertThat(result.FloorLost).IsEqual(0);

        // 20 jumps and landings: never below the ground, the ray down always hits.
        var jumps = 0;
        var airborne = false;
        var rayMisses = 0;
        var maxPenetration = 0.0;
        stepper.OnTick = _ =>
        {
            var jump = body.OnFloor && !airborne && jumps < 20;
            body.Step(Tick, jump);
            if (jump)
            {
                jumps++;
                airborne = true;
            }
            else if (airborne && body.OnFloor)
            {
                airborne = false;
            }

            if (TryRayDown(body, 8, out var ground))
            {
                maxPenetration = Math.Max(maxPenetration, -(body.GlobalPosition - ground).Dot(body.Up));
            }
            else
            {
                rayMisses++;
            }
        };
        AssertThat(await scene.WaitFramesUntil(() => jumps == 20 && !airborne, 20000)).IsTrue();
        stepper.OnTick = null;

        Record($"terrain110km distance_m={F(body.GlobalPosition.DistanceTo(Vector3.Zero))} max_jitter_m={F(result.MaxJitter)} floor_lost={result.FloorLost} max_penetration_m={F(maxPenetration)} ray_misses={rayMisses}");
        AssertThat(rayMisses).IsEqual(0);
        AssertThat(maxPenetration).IsLessEqual(0.05);
    }

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task FarFromOrigin_ConstructCollisionAt115km_CharacterStands()
    {
        await using var scene = EngineTestScene.Create();
        var catalog = BlockContentLoader.Load();
        using var world = new ConstructTestWorld(
            scene.Root, ConstructTestWorld.NewState(catalog), new ConstructWorldOptions { LoadAllWithoutViewers = true, Mode = ConstructWorldMode.CollisionOnly });

        // An 8x1x8 construct with K2-12 chunk collision; the construct world node carries it to 115 km (the planet frame keeps
        // its 100 km range, the offset lives only in the scene).
        var id = world.Create(new CellPos(0, 0, 0), origin: new System.Numerics.Vector3(0, 4000, 0));
        for (var x = 0; x < 8; x++)
        {
            for (var z = 0; z < 8; z++)
            {
                if (x != 0 || z != 0)
                {
                    world.Set(id, new CellPos(x, 0, z));
                }
            }
        }

        world.World.Position = new Vector3(115_000, -4000, 0);
        AssertThat(await scene.WaitFramesUntil(
            () => world.World.AreCollisionsReady(ConstructTestWorld.Planet, new System.Numerics.Vector3(4, 4001, 4), 8), 20000)).IsTrue();

        var top = new Vector3(115_004, 1, 4);
        var body = FarCharacter.Create(new GameRules(), Vector3.Zero, flatDown: true);
        scene.Add(body);
        body.Place(top + new Vector3(0, 0.05, 0));

        var result = await MeasureStandingAsync(scene, body);

        Record($"construct115km max_jitter_m={F(result.MaxJitter)} floor_lost={result.FloorLost} height_m={F(body.GlobalPosition.Y - top.Y)}");
        AssertThat(result.Settled).IsTrue();
        AssertThat(result.MaxJitter).IsLessEqual(JitterLimitMeters);
        AssertThat(result.FloorLost).IsEqual(0);
        AssertThat(Math.Abs(body.GlobalPosition.Y - top.Y)).IsLess(0.05);
    }

    /// <summary>Settles the body on the floor, then measures the largest drift from the settled position over 600 ticks.</summary>
    private static async Task<StandingResult> MeasureStandingAsync(EngineTestScene scene, FarCharacter body, PhysicsStepper? stepper = null)
    {
        stepper ??= scene.Add(new PhysicsStepper());
        var floorTicks = 0;
        var measured = 0;
        var floorLost = 0;
        var maxJitter = 0.0;
        var reference = Vector3.Zero;
        stepper.OnTick = _ =>
        {
            body.Step(Tick);
            if (floorTicks < SettleTicks)
            {
                floorTicks = body.OnFloor ? floorTicks + 1 : 0;
                reference = body.GlobalPosition;
                return;
            }

            if (measured < MeasureTicks)
            {
                measured++;
                maxJitter = Math.Max(maxJitter, body.GlobalPosition.DistanceTo(reference));
                floorLost += body.OnFloor ? 0 : 1;
            }
        };

        var settled = await scene.WaitFramesUntil(() => measured >= MeasureTicks, 20000);
        stepper.OnTick = null;
        return new StandingResult(settled, maxJitter, floorLost);
    }

    private static bool TryRayDown(FarCharacter body, double length, out Vector3 hit)
    {
        var up = body.Up;
        var from = body.GlobalPosition + (up * 2);
        var query = PhysicsRayQueryParameters3D.Create(from, from - (up * (length + 2)), CollisionLayers.TerrainMask | CollisionLayers.ConstructsMask);
        var result = body.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count == 0)
        {
            hit = default;
            return false;
        }

        hit = result["position"].AsVector3();
        return true;
    }

    private static string F(double value) => value.ToString("F5", CultureInfo.InvariantCulture);

    private static void Record(string line)
    {
        GD.Print($"[K5-20 far-from-origin] {line}");
        if (System.Environment.GetEnvironmentVariable("HORYZONT_RECON_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + "\n");
        }
    }

    private readonly record struct StandingResult(bool Settled, double MaxJitter, int FloorLost);
}
