using System.Globalization;

using GdUnit4;

using Godot;

using Horyzont.Client;
using Horyzont.Client.Camera;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Character;
using Horyzont.Game.Frames;
using Horyzont.Game.Physics;
using Horyzont.Game.Planet.Diagnostics;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Server.Characters;
using Horyzont.Game.Tests.Character.Fixtures;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Terrain;

using static GdUnit4.Assertions;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class WalkTests
{
    private const float Radius = 4000f;

    /// <summary>The camera of the third-person player follows a path over a pole of a smooth planet without a single jump.</summary>
    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task PlayerBody_WalkAcrossPole_NoCameraJump()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene, generator: () => SphereGenerator.Create(Radius));

        // 150 m before the pole on the meridian in the XY plane, 300 m of sprinting across it.
        var theta = -150f / Radius;
        var start = new Vector3(MathF.Sin(theta), MathF.Cos(theta), 0f) * (Radius + 2f);
        var run = new WalkRun(scene, start, new MovementRules());

        await run.StartAsync();
        await run.WalkAsync(2400);

        Record($"pole_walk jumps={run.Monitor.JumpCount} up_mismatch={run.UpMismatches} on_floor={run.FloorTicks}/{run.WalkTicks} min_x={run.MinX.ToString("F1", CultureInfo.InvariantCulture)} max_x={run.MaxX.ToString("F1", CultureInfo.InvariantCulture)}");
        AssertThat(run.MinX).IsLess(-100f);
        AssertThat(run.MaxX).IsGreater(100f);
        AssertThat(run.Monitor.JumpCount).IsEqual(0);
        AssertThat(run.UpMismatches).IsEqual(0);
        AssertThat(run.FloorTicks).IsGreater((int)(run.WalkTicks * 0.95));
        AssertThat(run.MaxBodyTilt).IsLess(1e-3f);
    }

    /// <summary>Walks the noisy planet of the K2-08 profile (four times the normal speed to keep the test short).</summary>
    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task PlayerBody_Walk2kmOnNoiseTerrain_CameraContinuity()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene, generator: () => GeneratorProfiles.CreateGenerator(TerrainTestWorld.Planet));
        var (start, _) = SpawnLocator.ForNewPlayer(TerrainTestWorld.Planet);
        var movement = new MovementRules();
        var fast = movement with
        {
            WalkSpeed = movement.WalkSpeed * 4f,
            SprintSpeed = movement.SprintSpeed * 4f,
            GroundAcceleration = movement.GroundAcceleration * 4f,
            GroundDeceleration = movement.GroundDeceleration * 4f,
        };
        var run = new WalkRun(scene, start, fast);
        using var generator = GeneratorProfiles.CreateGenerator(TerrainTestWorld.Planet);
        using var probe = new GeneratorProbe(generator);
        var buried = 0;
        run.OnTick = tick =>
        {
            if (tick % 15 == 0)
            {
                var feet = run.Body.Feet;
                if (probe.Sample((int)MathF.Round(feet.X), (int)MathF.Round(feet.Y), (int)MathF.Round(feet.Z)).Sdf < -1.5f)
                {
                    buried++;
                }
            }
        };

        await run.StartAsync();
        await run.WalkAsync(4000);

        var travelled = Vector3.Distance(run.Start, run.Body.Feet);
        Record($"noise_walk jumps={run.Monitor.JumpCount} buried={buried} travelled_m={travelled.ToString("F0", CultureInfo.InvariantCulture)} on_floor={run.FloorTicks}/{run.WalkTicks} up_mismatch={run.UpMismatches}");
        AssertThat(travelled).IsGreater(150f);
        AssertThat(run.Monitor.JumpCount).IsEqual(0);
        AssertThat(buried).IsEqual(0);
        AssertThat(run.UpMismatches).IsEqual(0);
    }

    private static void Record(string line)
    {
        if (System.Environment.GetEnvironmentVariable("HORYZONT_RECON_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + "\n");
        }
    }

    /// <summary>A body that walks forward with the client-side orientation and camera math, one step per physics frame.</summary>
    private sealed class WalkRun
    {
        private readonly EngineTestScene _scene;
        private readonly MovementRules _movement;
        private readonly OrientationTracker _tracker;
        private readonly CameraUpSmoother _smoother = new();
        private readonly GroundProbe _probe;
        private readonly PhysicsStepper _stepper;
        private readonly GameRules _rules = new();
        private int _target;
        private bool _grounded;
        private bool _walking;

        public WalkRun(EngineTestScene scene, Vector3 start, MovementRules movement)
        {
            _scene = scene;
            _movement = movement;
            Start = start;
            var planet = TerrainTestWorld.Planet;
            Body = PlayerBody.Instantiate();
            Body.Configure(planet, _rules, movement, false, CollisionLayers.ClientBodies, 1f / 60f);
            scene.Add(Body);
            Body.AddChild(new VoxelViewer { ViewDistance = 64, RequiresVisuals = false, RequiresCollisions = true });
            _tracker = new OrientationTracker(movement);
            _probe = new GroundProbe(planet.Id);
            Monitor = new CameraContinuityMonitor((MathF.Max(movement.MaxFallSpeed, movement.SprintSpeed) * 1f) + 5f, 1f / 60f);
            _stepper = scene.Add(new PhysicsStepper());
        }

        public Vector3 Start { get; }

        public PlayerBody Body { get; }

        public CameraContinuityMonitor Monitor { get; }

        public int UpMismatches { get; private set; }

        public int FloorTicks { get; private set; }

        public int WalkTicks { get; private set; }

        public float MinX { get; private set; } = float.MaxValue;

        public float MaxX { get; private set; } = float.MinValue;

        public float MaxBodyTilt { get; private set; }

        public Action<long>? OnTick { get; set; }

        public async Task StartAsync()
        {
            var up = TerrainMath.UpAt(Start);
            Body.Place(Start, Vector3.Zero, SurfaceFrame.FromYaw(up, 0f), MovementMode.Frozen, false);
            _tracker.Reset(SurfaceFrame.FromYaw(up, 0f), 0f);
            _smoother.Snap(up);
            _stepper.OnTick = Tick;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!_grounded && watch.Elapsed.TotalSeconds < 90)
            {
                await _scene.WaitFrames(1);
            }

            AssertThat(_grounded).IsTrue();
        }

        public async Task WalkAsync(int ticks)
        {
            _target = ticks;
            _walking = true;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (WalkTicks < ticks && watch.Elapsed.TotalSeconds < 300)
            {
                await _scene.WaitFrames(1);
            }

            _walking = false;
            AssertThat(WalkTicks).IsGreaterEqual(ticks);
        }

        private void Tick(long tick)
        {
            if (!_grounded)
            {
                if (_probe.TryFindGround(Body.GetWorld3D(), Body.Feet, 8f, out var ground))
                {
                    var up = TerrainMath.UpAt(ground);
                    Body.Place(ground + (up * 0.05f), Vector3.Zero, SurfaceFrame.FromYaw(up, 0f), MovementMode.Walking, false);
                    _tracker.Reset(SurfaceFrame.FromYaw(up, 0f), 0f);
                    _smoother.Snap(up);
                    Monitor.Snap();
                    _grounded = true;
                }

                return;
            }

            if (!_walking || WalkTicks >= _target)
            {
                return;
            }

            var feet = Body.Feet;
            var upBefore = TerrainMath.UpAt(feet);
            _tracker.Advance(upBefore, 0f, 0f);
            var wire = new PlayerInput(0, 0f, 1f, _tracker.YawFor(upBefore), 0f, InputButtons.Sprint);
            MotorInput.TryFrom(wire, _movement, out var motor);
            Body.Step(motor);
            WalkTicks++;
            if (Body.OnFloor)
            {
                FloorTicks++;
            }

            var engineUp = PlanetFrames.ToPlanetLocalDirection(TerrainTestWorld.Planet.Id, Body.UpDirection);
            if (Vector3.Distance(engineUp, upBefore) > 1e-3f)
            {
                UpMismatches++;
            }

            var bodyUp = Vector3.Transform(Vector3.UnitY, Body.BodyRotation);
            MaxBodyTilt = MathF.Max(MaxBodyTilt, Vector3.Distance(bodyUp, upBefore));
            var after = Body.Feet;
            MinX = MathF.Min(MinX, after.X);
            MaxX = MathF.Max(MaxX, after.X);
            _smoother.Advance(TerrainMath.UpAt(after), 1f / 60f);
            var pose = CameraRigMath.Compute(after, _tracker.BodyRotation, 0f, _smoother.Up, CameraMode.ThirdPerson, TppOffsets.Default, _rules);
            Monitor.Observe(pose.Origin, pose.Direction, 0f);
            OnTick?.Invoke(tick);
        }
    }
}
