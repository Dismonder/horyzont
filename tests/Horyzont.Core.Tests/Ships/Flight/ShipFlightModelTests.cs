using System.Numerics;

using FsCheck.Fluent;

using Horyzont.Core.Frames;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Tests.Serialization;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing.Ships;

namespace Horyzont.Core.Tests.Ships.Flight;

/// <summary>K5-24 decision 6: the flight model.</summary>
public sealed class ShipFlightModelTests
{
    private const float Dt = 1f / 60f;
    private static readonly FlightRules _rules = FlightRules.Default;

    private static ShipControl Control(sbyte tx = 0, sbyte ty = 0, sbyte tz = 0, ShipButtons buttons = ShipButtons.None, sbyte pitch = 0, sbyte yaw = 0, sbyte roll = 0) =>
        new(tx, ty, tz, pitch, yaw, roll, buttons);

    private static double Run(ref ShipMotion motion, ShipControl control, ShipStats stats, Func<ShipMotion, FlightEnvironment> environment, int steps)
    {
        double limit = 0;
        for (var i = 0; i < steps; i++)
        {
            var e = environment(motion);
            limit = ShipFlightModel.Step(ref motion, control, stats, e, _rules, Dt).SpeedLimit;
        }

        return limit;
    }

    private static ShipControl RandomControl(Random random) =>
        new((sbyte)random.Next(-128, 128), (sbyte)random.Next(-128, 128), (sbyte)random.Next(-128, 128),
            (sbyte)random.Next(-128, 128), (sbyte)random.Next(-128, 128), (sbyte)random.Next(-128, 128), (ShipButtons)random.Next(0, 8));

    private static ShipMotion Simulate(int seed, int steps)
    {
        var random = new Random(seed);
        var stats = FlightFixtures.Thirty();
        var motion = FlightFixtures.AboveHome(2000);
        for (var i = 0; i < steps; i++)
        {
            var control = (i % 30) == 0 ? RandomControl(random) : default;
            ShipFlightModel.Step(ref motion, control, stats, FlightFixtures.HomeEnvironment(motion), _rules, Dt);
        }

        return motion;
    }

    [Fact]
    public void ShipFlightModel_SameInputs_BitwiseIdentical()
    {
        var a = Simulate(7, 10_000);
        var b = Simulate(7, 10_000);

        Assert.Equal(BitConverter.DoubleToInt64Bits(a.Pose.Position.X), BitConverter.DoubleToInt64Bits(b.Pose.Position.X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(a.Pose.Position.Y), BitConverter.DoubleToInt64Bits(b.Pose.Position.Y));
        Assert.Equal(BitConverter.DoubleToInt64Bits(a.Pose.Rotation.W), BitConverter.DoubleToInt64Bits(b.Pose.Rotation.W));
        Assert.Equal(a.LinearVelocity, b.LinearVelocity);
        Assert.Equal(a.AngularVelocity, b.AngularVelocity);
        Assert.Equal(BitConverter.SingleToInt32Bits(a.Fuel), BitConverter.SingleToInt32Bits(b.Fuel));
        Assert.Equal(a.Flags, b.Flags);
        Assert.True(a.Pose.Position.IsFinite && a.Pose.Rotation.IsFinite);
    }

    [Fact]
    public void ShipFlightModel_Step_AllocatesZeroBytes()
    {
        var stats = FlightFixtures.Thirty();
        var motion = FlightFixtures.AboveHome(500);
        var control = Control(ty: 100, tz: -50, buttons: ShipButtons.Assist | ShipButtons.PulseDrive, pitch: 20);
        for (var i = 0; i < 100; i++)
        {
            var e = FlightFixtures.HomeEnvironment(motion);
            ShipFlightModel.Step(ref motion, control, stats, e, _rules, Dt);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            var e = FlightFixtures.HomeEnvironment(motion);
            ShipFlightModel.Step(ref motion, control, stats, e, _rules, Dt);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ShipFlightModel_SpeedNeverExceedsLimit()
    {
        var stats = FlightFixtures.Cube(thrustPerFace: 2_000_000f);
        var gen =
            from altitude in Gen.Choose(0, 60_000)
            from seed in Gen.Choose(0, int.MaxValue)
            select (altitude, seed);
        Kit.ForAll(gen, sample =>
        {
            var random = new Random(sample.seed);
            var motion = FlightFixtures.AboveHome(sample.altitude);
            for (var i = 0; i < 120; i++)
            {
                var control = RandomControl(random);
                var outcome = ShipFlightModel.Step(ref motion, control, stats, FlightFixtures.HomeEnvironment(motion), _rules, Dt);
                Assert.True(motion.LinearVelocity.Length() <= outcome.SpeedLimit + 1e-3, $"{motion.LinearVelocity.Length()} > {outcome.SpeedLimit}");
            }
        }, 100);
    }

    [Fact]
    public void ShipFlightModel_DescentAtLimit_HalvingTakesTauLn2()
    {
        var stats = FlightFixtures.Cube();
        var motion = FlightFixtures.AboveHome(8000);
        // Nose down: the pulse drive holds the limit h/τ (1000 m/s at 8 km is under the pulse cap).
        motion.Pose = motion.Pose with { Rotation = QuaternionD.FromAxisAngle(new Vector3D(1, 0, 0), -Math.PI / 2) };
        motion.LinearVelocity = new Vector3(0, -1000f, 0);
        var steps = 0;
        while (FlightFixtures.HomeEnvironment(motion).Altitude > 1000)
        {
            ShipFlightModel.Step(ref motion, Control(buttons: ShipButtons.PulseDrive), stats, FlightFixtures.HomeEnvironment(motion), _rules, Dt);
            steps++;
        }

        var expected = _rules.TauSeconds * Math.Log(8);
        Assert.InRange(steps * Dt, expected * 0.95, expected * 1.05);
    }

    [Fact]
    public void ShipFlightModel_Assist_DampsDriftAndRotation()
    {
        var stats = FlightFixtures.Cube();
        var motion = new ShipMotion
        {
            Pose = PoseD.Identity,
            LinearVelocity = new Vector3(10, 0, 5),
            AngularVelocity = new Vector3(0, 1, 0.5f),
            Fuel = 2000,
        };

        Run(ref motion, Control(buttons: ShipButtons.Assist), stats, _ => FlightFixtures.Void(), 300);

        Assert.True(motion.LinearVelocity.Length() < 0.5f, $"v = {motion.LinearVelocity}");
        Assert.True(motion.AngularVelocity.Length() < 0.05f, $"w = {motion.AngularVelocity}");
    }

    [Fact]
    public void ShipFlightModel_Assist_HoversWhenDownThrustExceedsWeight()
    {
        var stats = FlightFixtures.Cube(upThrust: 200_000f);
        var motion = FlightFixtures.AboveHome(500);
        var start = motion.Pose.Position.Y;

        Run(ref motion, Control(buttons: ShipButtons.Assist), stats, m => FlightFixtures.HomeEnvironment(m), 600);

        Assert.True(Math.Abs(motion.LinearVelocity.Y) < 0.1f, $"v = {motion.LinearVelocity}");
        Assert.InRange(motion.Pose.Position.Y, start - 5, start + 1);
    }

    [Fact]
    public void ShipFlightModel_Assist_NoHoverWhenThrustInsufficient()
    {
        var stats = FlightFixtures.Cube(upThrust: 50_000f);
        var motion = FlightFixtures.AboveHome(500);

        Run(ref motion, Control(buttons: ShipButtons.Assist), stats, m => FlightFixtures.HomeEnvironment(m), 180);

        Assert.True(motion.LinearVelocity.Y < -5f, $"v = {motion.LinearVelocity}");
    }

    [Fact]
    public void ShipFlightModel_Fuel_ProportionalToThrust()
    {
        var stats = FlightFixtures.Cube();
        var full = new ShipMotion { Pose = PoseD.Identity, Fuel = 2000 };
        var half = full;

        Run(ref full, Control(tx: 127), stats, _ => FlightFixtures.Void(), 60);
        Run(ref half, Control(tx: 64), stats, _ => FlightFixtures.Void(), 60);

        Assert.Equal(200_000 * 4e-6, 2000 - full.Fuel, 2);
        Assert.Equal(200_000 * 4e-6 * 64 / 127, 2000 - half.Fuel, 2);
    }

    [Fact]
    public void ShipFlightModel_Fuel_PulseConsumesFaster()
    {
        var stats = FlightFixtures.Cube();
        var pulse = new ShipMotion { Pose = PoseD.Identity, Fuel = 2000 };
        var thrust = pulse;

        Run(ref pulse, Control(buttons: ShipButtons.PulseDrive), stats, _ => FlightFixtures.Void(), 60);
        Run(ref thrust, Control(tz: -127), stats, _ => FlightFixtures.Void(), 60);

        Assert.Equal(_rules.PulseFuelPerSecond, 2000 - pulse.Fuel, 2);
        Assert.True(2000 - pulse.Fuel > 2 * (2000 - thrust.Fuel));
        Assert.True((pulse.Flags & ShipFlightFlags.PulseEngaged) != 0);
        Assert.True(pulse.LinearVelocity.Z < -500f, $"v = {pulse.LinearVelocity}");
    }

    [Fact]
    public void ShipFlightModel_Fuel_ReserveOnlyLandingAndBlocksLaunch()
    {
        var stats = FlightFixtures.Cube();
        var motion = FlightFixtures.AboveHome(1000, fuel: 150);

        Run(ref motion, Control(ty: 127, buttons: ShipButtons.PulseDrive), stats, m => FlightFixtures.HomeEnvironment(m), 120);

        Assert.True(motion.LinearVelocity.Y <= 0f, $"v = {motion.LinearVelocity}");
        Assert.True((motion.Flags & ShipFlightFlags.Reserve) != 0);
        Assert.True((motion.Flags & ShipFlightFlags.PulseEngaged) == 0);

        var parked = FlightFixtures.AboveHome(0, fuel: 150);
        parked.Flags = ShipFlightFlags.Parked;
        var e = FlightFixtures.HomeEnvironment(parked) with { LaunchBlock = ShipLaunchBlock.FuelReserve };
        var start = parked.Pose;
        for (var i = 0; i < 60; i++)
        {
            ShipFlightModel.Step(ref parked, Control(ty: 127), stats, e, _rules, Dt);
        }

        Assert.True((parked.Flags & ShipFlightFlags.Parked) != 0);
        Assert.Equal(start, parked.Pose);
    }

    [Fact]
    public void ShipFlightModel_Fuel_EmptyNoThrust()
    {
        var stats = FlightFixtures.Cube();
        var motion = new ShipMotion { Pose = PoseD.Identity, Fuel = 0 };

        Run(ref motion, Control(tx: 127, ty: 127, buttons: ShipButtons.PulseDrive | ShipButtons.Assist), stats, _ => FlightFixtures.Void(), 60);

        Assert.Equal(Vector3.Zero, motion.LinearVelocity);
        Assert.Equal(0f, motion.Fuel);
    }

    [Fact]
    public void ShipFlightModel_Drag_OnlyInsideAtmosphere_MoonNone()
    {
        var home = FlightFixtures.Home;
        var low = FlightEnvironments.InPlanet(home, 0, new Vector3D(0, home.RadiusMeters + 100, 0), _rules);
        var top = FlightEnvironments.InPlanet(home, 0, new Vector3D(0, home.RadiusMeters + (0.9 * home.AtmosphereHeightMeters), 0), _rules);
        var outside = FlightEnvironments.InPlanet(home, 0, new Vector3D(0, home.RadiusMeters + home.AtmosphereHeightMeters + 10, 0), _rules);
        var moon = FlightEnvironments.InPlanet(home, 1, new Vector3D(0, home.RadiusMeters + 100, 0), _rules);

        Assert.Equal(0.05f, low.DragPerSecond);
        Assert.Equal(0.025f, top.DragPerSecond, 4);
        Assert.Equal(0f, outside.DragPerSecond);
        Assert.Equal(0f, moon.DragPerSecond);

        var stats = FlightFixtures.Cube();
        var drag = new ShipMotion { Pose = PoseD.Identity, LinearVelocity = new Vector3(50, 0, 0), Fuel = 2000 };
        var none = drag;
        Run(ref drag, default, stats, _ => FlightFixtures.Void() with { DragPerSecond = 0.05f }, 60);
        Run(ref none, default, stats, _ => FlightFixtures.Void(), 60);
        Assert.Equal(50 * Math.Exp(-0.05), drag.LinearVelocity.X, 1);
        Assert.Equal(50f, none.LinearVelocity.X);
    }

    [Fact]
    public void ShipFlightModel_GateNotLoaded_StaysAbove2R()
    {
        var stats = FlightFixtures.Cube();
        var home = FlightFixtures.Home;
        var motion = new ShipMotion { Pose = new PoseD(new Vector3D(0, 2.05 * home.RadiusMeters, 0), QuaternionD.Identity), Fuel = 2000 };

        Run(ref motion, Control(ty: -127), stats, m => FlightFixtures.HomeEnvironment(m) with { Gate = ShipTerrainGate.NotLoaded }, 600);

        Assert.InRange(motion.Pose.Position.Y, (2 * home.RadiusMeters) - 3, 2 * home.RadiusMeters);
    }

    [Fact]
    public void ShipFlightModel_CollisionPending_NoDescent()
    {
        var stats = FlightFixtures.Cube();
        var motion = FlightFixtures.AboveHome(300);
        var start = motion.Pose.Position.Y;

        Run(ref motion, Control(ty: -127), stats, m => FlightFixtures.HomeEnvironment(m) with { Gate = ShipTerrainGate.CollisionPending }, 300);

        Assert.True(motion.Pose.Position.Y >= start - 1e-6, $"{motion.Pose.Position.Y} < {start}");
    }

    [Fact]
    public void ShipFlightModel_GateUnavailable_NoDescentAnyAltitude()
    {
        var stats = FlightFixtures.Cube();
        var motion = FlightFixtures.AboveHome(5000);
        var start = motion.Pose.Position.Y;

        Run(ref motion, Control(ty: -127), stats, m => FlightFixtures.HomeEnvironment(m) with { Gate = ShipTerrainGate.Unavailable }, 300);

        Assert.True(motion.Pose.Position.Y >= start - 1e-6);
    }

    [Fact]
    public void ShipFlightModel_LaunchBlockInEnvironment_KeepsParked()
    {
        var stats = FlightFixtures.Cube();
        var motion = FlightFixtures.AboveHome(0);
        motion.Flags = ShipFlightFlags.Parked | ShipFlightFlags.GearDown;
        var blocked = motion;

        Run(ref blocked, Control(ty: 127), stats, m => FlightFixtures.HomeEnvironment(m) with { LaunchBlock = ShipLaunchBlock.FuelReserve }, 60);
        Run(ref motion, Control(ty: 127), stats, m => FlightFixtures.HomeEnvironment(m), 60);

        Assert.True((blocked.Flags & ShipFlightFlags.Parked) != 0);
        Assert.Equal(Vector3.Zero, blocked.LinearVelocity);
        Assert.True((motion.Flags & ShipFlightFlags.Parked) == 0);
        Assert.True(motion.LinearVelocity.Y > 1f);
    }

    [Fact]
    public void ShipFlightModel_SystemBoundary_NoOutwardMotion()
    {
        var stats = FlightFixtures.Cube();
        var motion = new ShipMotion { Pose = new PoseD(new Vector3D(401_000, 0, 0), QuaternionD.Identity), LinearVelocity = new Vector3(200, 0, 50), Fuel = 2000 };
        var e = FlightFixtures.Void() with { InSystemFrame = true, SystemDistance = 401_000 };

        Run(ref motion, Control(tx: 127), stats, _ => e, 120);

        Assert.True(motion.Pose.Position.X <= 401_000 + 1e-6);
        Assert.True(motion.Pose.Position.Z > 0);
    }

    [Fact]
    public void ShipFlightModel_HorizontalSpeedCap_LimitsMotionAcrossUp()
    {
        var stats = FlightFixtures.Cube();
        var motion = FlightFixtures.AboveHome(200);
        motion.LinearVelocity = new Vector3(90, -20, 0);

        Run(ref motion, Control(tx: 127), stats, m => FlightFixtures.HomeEnvironment(m) with { HorizontalSpeedCap = 30 }, 30);

        Assert.InRange(motion.LinearVelocity.X, 0, 30.01f);
    }

    [Fact]
    public void ShipStats_ControlFrame_FirstCockpitOrientation()
    {
        Assert.Equal(Quaternion.Identity, FlightFixtures.Thirty().ControlFrame);
        var yawed = ShipFixtures.Thirty.Select(b => b.Key == ShipFixtures.Cockpit ? b with { Orientation = BlockOrientation.WithYaw(0, 1) } : b).ToList();
        var world = ShipFixtures.CreateWorld(yawed);
        world.Constructs.TryGet(ShipFixtures.ConstructId, out var construct);
        var stats = ShipStatsCalculator.Compute(construct!, ShipFixtures.ContentFor(world));

        Assert.Equal(BlockOrientation.ToQuaternion(BlockOrientation.WithYaw(0, 1)), stats.ControlFrame);
        Assert.True(stats.BoundingRadius > 2.5f && stats.BoundingRadius < 5f, $"{stats.BoundingRadius}");
    }
}
