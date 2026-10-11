using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ships.Flight;
using Horyzont.Testing.Ships;

namespace Horyzont.Core.Tests.Ships.Flight;

/// <summary>K5-24 decision 7: a ship without a pilot lands vertically and parks; in vacuum it brakes to zero.</summary>
public sealed class AutolandTests
{
    private const float Dt = 1f / 60f;

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void Autoland_From100m1km5km_LandsVerticallyAndParks(double height)
    {
        var rules = FlightRules.Default;
        var stats = FlightFixtures.Thirty();
        var home = FlightFixtures.Home;
        var collider = new SphereGroundCollider().Add(Vector3D.Zero, home.RadiusMeters);
        var tilt = QuaternionD.FromAxisAngle(new Vector3D(0, 0, 1), 0.3);
        var motion = new ShipMotion
        {
            Pose = new PoseD(new Vector3D(0, home.RadiusMeters + height, 0), tilt),
            LinearVelocity = new Vector3(0, -10, 0),
            Fuel = 2000,
        };
        var start = motion.Pose.Position;
        var maxDrift = 0.0;
        for (var i = 0; i < 60 * 600 && (motion.Flags & ShipFlightFlags.Parked) == 0; i++)
        {
            var e = FlightFixtures.HomeEnvironment(motion, rules);
            var control = AutolandController.Compute(motion, e, stats, rules);
            var from = motion.Pose;
            ShipFlightModel.Step(ref motion, control, stats, e, rules, Dt);
            ShipContact.Resolve(ref motion, from, collider.Sweep(from, motion.Pose, stats), e, rules);
            var p = motion.Pose.Position;
            maxDrift = Math.Max(maxDrift, Math.Sqrt((p.X * p.X) + (p.Z * p.Z)));
        }

        Assert.True((motion.Flags & ShipFlightFlags.Parked) != 0, $"not parked at {motion.Pose.Position}, v = {motion.LinearVelocity}");
        Assert.Equal(Vector3.Zero, motion.LinearVelocity);
        Assert.True(maxDrift <= 5, $"drift {maxDrift} m");
        Assert.True(motion.Pose.Position.Y < start.Y);
        var up = motion.Pose.Rotation.Rotate(new Vector3D(0, 1, 0));
        Assert.True(up.Y > 0.99, $"up {up}");
    }

    [Fact]
    public void Autoland_ZeroGravity_StopsToZero()
    {
        var rules = FlightRules.Default;
        var stats = FlightFixtures.Thirty();
        var motion = new ShipMotion
        {
            Pose = PoseD.Identity,
            LinearVelocity = new Vector3(5, -3, 2),
            AngularVelocity = new Vector3(0.2f, 0, -0.1f),
            Fuel = 2000,
        };

        for (var i = 0; i < 60 * 30; i++)
        {
            var e = FlightFixtures.Void();
            ShipFlightModel.Step(ref motion, AutolandController.Compute(motion, e, stats, rules), stats, e, rules, Dt);
        }

        Assert.True(motion.LinearVelocity.Length() < 0.01f, $"v = {motion.LinearVelocity}");
        Assert.True(motion.AngularVelocity.Length() < 0.01f, $"w = {motion.AngularVelocity}");
        Assert.True((motion.Flags & ShipFlightFlags.GearDown) == 0);
    }
}
