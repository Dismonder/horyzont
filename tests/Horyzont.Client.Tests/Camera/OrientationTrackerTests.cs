using System.Numerics;

using Horyzont.Client.Camera;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Testing.Motion;

namespace Horyzont.Client.Tests.Camera;

public sealed class OrientationTrackerTests
{
    private const float Dt = 1f / 60f;

    private static readonly MovementRules _rules = new();

    private static float AngleBetween(Vector3 a, Vector3 b) => MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b));

    [Fact]
    public void OrientationTracker_MouseYaw_RotatesAboutLocalUp()
    {
        var up = Vector3.Normalize(new Vector3(1f, 2f, -1f));
        var tracker = new OrientationTracker(_rules);
        tracker.Reset(SurfaceFrame.FromYaw(up, 0.2f), 0f);
        var forwardBefore = Vector3.Transform(-Vector3.UnitZ, tracker.BodyRotation);

        tracker.Advance(up, 0.5f, 0f);

        Assert.Equal(0.7f, tracker.YawFor(up), 1e-4f);
        Assert.True((Vector3.Transform(Vector3.UnitY, tracker.BodyRotation) - up).Length() < 1e-5f);
        Assert.Equal(0.5f, AngleBetween(forwardBefore, Vector3.Transform(-Vector3.UnitZ, tracker.BodyRotation)), 1e-4f);
        Assert.Equal(0f, tracker.Pitch);
    }

    [Fact]
    public void OrientationTracker_PitchClamped()
    {
        var up = Vector3.UnitX;
        var tracker = new OrientationTracker(_rules);
        tracker.Reset(SurfaceFrame.FromYaw(up, 0f), 0f);

        tracker.Advance(up, 0f, 5f);
        Assert.Equal(_rules.MaxPitch, tracker.Pitch);

        tracker.Advance(up, 0f, -10f);
        Assert.Equal(-_rules.MaxPitch, tracker.Pitch);

        tracker.Reset(tracker.BodyRotation, 9f);
        Assert.Equal(_rules.MaxPitch, tracker.Pitch);
    }

    [Fact]
    public void OrientationTracker_AdvanceToNewUp_KeepsBodyUpOnSurfaceUp()
    {
        var tracker = new OrientationTracker(_rules);
        tracker.Reset(SurfaceFrame.FromYaw(Vector3.UnitX, 0.4f), 0f);
        var newUp = Vector3.Normalize(new Vector3(1f, 0.1f, 0.05f));

        tracker.Advance(newUp, 0f, 0f);

        Assert.True((Vector3.Transform(Vector3.UnitY, tracker.BodyRotation) - newUp).Length() < 1e-5f);
        Assert.True(MathF.Abs(tracker.BodyRotation.Length() - 1f) < 1e-6f);
    }

    [Fact]
    public void OrientationTracker_WalkOverPole_CameraForwardContinuous()
    {
        // 60 m before the pole of the reference frame, straight over it and 190 m beyond; no mouse movement.
        var planet = new PlanetParams(new PlanetId(1), new SystemId(1), 1, "core:test/1");
        const float delta = 60f / 4000f;
        var body = IdealSphereBody.OnSurface(planet, _rules, new Vector3(MathF.Sin(delta), MathF.Cos(delta), 0f));
        var tracker = new OrientationTracker(_rules);
        tracker.Reset(SurfaceFrame.FromYaw(TerrainMath.UpAt(body.Feet), 0f), 0f);
        var rules = new GameRules();
        var previousDirection = Vector3.Zero;
        var previousYaw = 0f;
        var worstTurn = 0f;
        var worstYawJump = 0f;
        var crossedPole = false;

        for (var tick = 0; tick < 2000; tick++)
        {
            var up = TerrainMath.UpAt(body.Feet);
            tracker.Advance(up, 0f, 0f);
            var yaw = tracker.YawFor(up);
            body.Step(new MotorInput(0f, 1f, yaw, 0f, InputButtons.Sprint), devFlightAllowed: false, Dt);

            var pose = CameraRigMath.Compute(
                body.Feet, tracker.BodyRotation, tracker.Pitch, TerrainMath.UpAt(body.Feet), CameraMode.FirstPerson, TppOffsets.Default, rules);
            if (tick > 0)
            {
                worstTurn = MathF.Max(worstTurn, AngleBetween(previousDirection, pose.Direction) * 180f / MathF.PI);
                worstYawJump = MathF.Max(worstYawJump, MathF.Abs(yaw - previousYaw));
            }

            crossedPole |= body.Feet.X < 0f;
            previousDirection = pose.Direction;
            previousYaw = yaw;
        }

        Assert.True(crossedPole, "the path must go over the pole");
        Assert.True(worstYawJump > 1f, $"the Yaw value is expected to jump at the pole (largest change {worstYawJump})");
        Assert.True(worstTurn <= 0.05f, $"camera turned {worstTurn} degrees in one tick");
    }
}
