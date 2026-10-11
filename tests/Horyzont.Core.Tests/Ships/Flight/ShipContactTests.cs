using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Core.Tests.Ships.Flight;

/// <summary>K5-24 decision 9: the response to a sweep hit (Opus review fix: a hit at the start keeps the start rotation).</summary>
public sealed class ShipContactTests
{
    private static readonly FlightEnvironment _wallEnvironment = new() { Up = new Vector3D(0, 1, 0) };
    private static readonly QuaternionD _yaw = QuaternionD.FromAxisAngle(new Vector3D(0, 1, 0), Math.PI / 4);

    [Fact]
    public void ShipContact_HitAtFractionZero_KeepsStartPoseAndRotation()
    {
        var from = new PoseD(new Vector3D(9, 0, 0), QuaternionD.Identity);
        var motion = new ShipMotion { Pose = new PoseD(new Vector3D(10, 0, 0), _yaw), LinearVelocity = new Vector3(1, 0, 0) };

        ShipContact.Resolve(ref motion, from, new ShipSweepResult(true, 0, new Vector3D(-1, 0, 0)), _wallEnvironment, FlightRules.Default);

        Assert.Equal(from.Position, motion.Pose.Position);
        Assert.Equal(QuaternionD.Identity, motion.Pose.Rotation);
    }

    [Fact]
    public void ShipContact_HitPartWay_KeepsStepRotation()
    {
        var from = new PoseD(new Vector3D(0, 0, 0), QuaternionD.Identity);
        var motion = new ShipMotion { Pose = new PoseD(new Vector3D(10, 0, 0), _yaw), LinearVelocity = new Vector3(1, 0, 0) };

        ShipContact.Resolve(ref motion, from, new ShipSweepResult(true, 0.5, new Vector3D(-1, 0, 0)), _wallEnvironment, FlightRules.Default);

        Assert.Equal(5, motion.Pose.Position.X, 9);
        Assert.Equal(_yaw, motion.Pose.Rotation);
    }

    [Fact]
    public void ShipContact_Friction_DampsOnlyTangentialVelocity()
    {
        var rules = FlightRules.Default;
        var from = new PoseD(Vector3D.Zero, QuaternionD.Identity);

        // Into the wall: the normal part is removed, the tangential part is multiplied by the friction.
        var into = new ShipMotion { Pose = new PoseD(new Vector3D(-1, 0, 1), QuaternionD.Identity), LinearVelocity = new Vector3(-3, 0, 4) };
        ShipContact.Resolve(ref into, from, new ShipSweepResult(true, 0.5, new Vector3D(1, 0, 0)), _wallEnvironment, rules);
        Assert.Equal(0f, into.LinearVelocity.X, 5);
        Assert.Equal((float)(4 * rules.ContactFriction), into.LinearVelocity.Z, 5);

        // Moving away from the contact: the separating normal part is kept (no friction on it), the tangential part is damped.
        var away = new ShipMotion { Pose = new PoseD(new Vector3D(1, 0, 1), QuaternionD.Identity), LinearVelocity = new Vector3(3, 0, 4) };
        ShipContact.Resolve(ref away, from, new ShipSweepResult(true, 0.5, new Vector3D(1, 0, 0)), _wallEnvironment, rules);
        Assert.Equal(3f, away.LinearVelocity.X, 5);
        Assert.Equal((float)(4 * rules.ContactFriction), away.LinearVelocity.Z, 5);
    }
}
