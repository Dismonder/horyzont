using System.Globalization;

using GdUnit4;

using Godot;

using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Character;
using Horyzont.Game.Physics;
using Horyzont.Game.Tests.Character.Fixtures;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Terrain;

using static GdUnit4.Assertions;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class PlayerBodyTests
{
    private const float Radius = 4000f;

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task PlayerBody_SpawnAboveVoxelSphere_FrozenUntilCollision_ThenStandsWithin5cm()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene, generator: () => SphereGenerator.Create(Radius));
        var planet = TerrainTestWorld.Planet;
        var rules = new GameRules();
        var body = PlayerBody.Instantiate();
        body.Configure(planet, rules, new MovementRules(), false, CollisionLayers.ServerBodies, 1f / 60f);
        var up = Vector3.UnitX;
        scene.Add(body);
        var spawn = new Vector3(Radius + 2f, 0f, 0f);
        body.Place(spawn, Vector3.Zero, SurfaceFrame.FromYaw(up, 0f), MovementMode.Frozen, false);
        var viewer = new VoxelViewer { ViewDistance = 64, RequiresVisuals = false, RequiresCollisions = true };
        body.AddChild(viewer);
        var probe = new GroundProbe(planet.Id);
        var stepper = scene.Add(new PhysicsStepper());
        var groundFoundAtTick = -1L;
        var groundHitOffset = float.NaN;
        var framesBefore = 0;
        stepper.OnTick = tick =>
        {
            if (body.Mode == MovementMode.Frozen)
            {
                if (probe.TryFindGround(body.GetWorld3D(), body.Feet, 8f, out var ground))
                {
                    groundFoundAtTick = tick;
                    groundHitOffset = ground.Length() - Radius;
                    body.Place(ground + (TerrainMath.UpAt(ground) * 0.05f), Vector3.Zero, body.BodyRotation, MovementMode.Walking, false);
                }
            }
            else
            {
                body.Step(MotorInput.Neutral(0f, 0f));
            }
        };

        // Spawn before collisions: while frozen the body must not move at all.
        var stayed = true;
        for (var i = 0; i < 20000 && groundFoundAtTick < 0; i++)
        {
            await scene.WaitFrames(1);
            framesBefore++;
            stayed &= groundFoundAtTick >= 0 || body.Feet.Equals(spawn);
        }

        AssertThat(groundFoundAtTick).IsGreater(0L);
        AssertThat(stayed).IsTrue();
        AssertThat(Math.Abs(groundHitOffset)).IsLess(0.5f);

        var settleTicks = stepper.Ticks + 120;
        await WaitTicks(scene, stepper, settleTicks);

        var height = body.Feet.Length() - Radius;
        AssertThat(height).IsGreaterEqual(-0.05f);
        AssertThat(height).IsLess(0.05f);
        AssertThat(body.OnFloor).IsTrue();
        Record($"ground_found_tick={groundFoundAtTick} frames={framesBefore} hit_offset={groundHitOffset.ToString("F3", CultureInfo.InvariantCulture)} settled_height={height.ToString("F4", CultureInfo.InvariantCulture)}");
    }

    /// <summary>Recon (d): many <c>MoveAndSlide</c> calls in one tick (the replay after a correction) see the new body position.</summary>
    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task PlayerBody_RepeatedStepsInOneTick_MatchOneStepPerTick()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene, generator: () => SphereGenerator.Create(Radius));
        var planet = TerrainTestWorld.Planet;
        var body = PlayerBody.Instantiate();
        body.Configure(planet, new GameRules(), new MovementRules(), false, CollisionLayers.ServerBodies, 1f / 60f);
        scene.Add(body);
        var up = Vector3.UnitX;
        var rotation = SurfaceFrame.FromYaw(up, 0f);
        body.Place(new Vector3(Radius + 2f, 0f, 0f), Vector3.Zero, rotation, MovementMode.Frozen, false);
        body.AddChild(new VoxelViewer { ViewDistance = 64, RequiresVisuals = false, RequiresCollisions = true });
        var probe = new GroundProbe(planet.Id);
        var stepper = scene.Add(new PhysicsStepper());
        var input = new MotorInput(0f, 1f, 0f, 0f, InputButtons.Sprint);
        const int Steps = 30;
        var phase = 0;
        var start = default(Vector3);
        var batch = default(Vector3);
        var perTick = default(Vector3);
        var stepsDone = 0;
        stepper.OnTick = _ =>
        {
            switch (phase)
            {
                case 0 when probe.TryFindGround(body.GetWorld3D(), body.Feet, 8f, out var ground):
                    start = ground + (up * 0.05f);
                    phase = 1;
                    break;
                case 1:
                    // Settle on the ground first.
                    body.Place(start, Vector3.Zero, rotation, MovementMode.Walking, false);
                    for (var i = 0; i < 10; i++)
                    {
                        body.Step(MotorInput.Neutral(0f, 0f));
                    }

                    start = body.Feet;
                    body.Place(start, Vector3.Zero, rotation, MovementMode.Walking, true);
                    for (var i = 0; i < Steps; i++)
                    {
                        body.Step(input);
                    }

                    batch = body.Feet;
                    body.Place(start, Vector3.Zero, rotation, MovementMode.Walking, true);
                    phase = 2;
                    break;
                case 2:
                    body.Step(input);
                    if (++stepsDone == Steps)
                    {
                        perTick = body.Feet;
                        phase = 3;
                    }

                    break;
                default:
                    break;
            }
        };

        await scene.WaitFramesUntil(() => phase == 3, 20000);

        AssertThat(phase).IsEqual(3);
        AssertThat(Vector3.Distance(batch, perTick)).IsLess(0.001f);
        AssertThat(Vector3.Distance(start, batch)).IsGreater(1f);
    }

    internal static async Task WaitTicks(EngineTestScene scene, PhysicsStepper stepper, long target, double seconds = 120)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (stepper.Ticks < target && watch.Elapsed.TotalSeconds < seconds)
        {
            await scene.WaitFrames(1);
        }

        AssertThat(stepper.Ticks).IsGreaterEqual(target);
    }

    private static void Record(string line)
    {
        if (System.Environment.GetEnvironmentVariable("HORYZONT_RECON_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + "\n");
        }
    }
}
