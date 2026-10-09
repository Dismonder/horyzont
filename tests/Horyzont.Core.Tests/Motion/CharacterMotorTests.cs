using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Testing.Motion;

namespace Horyzont.Core.Tests.Motion;

/// <summary>K2-11 step 2: the pure movement step, driven by <see cref="IdealSphereBody"/> instead of the engine.</summary>
public sealed class CharacterMotorTests
{
    private const float Dt = 1f / 60f;

    private static readonly PlanetParams _planet = new(new PlanetId(1), new SystemId(1), 1, "core:test/1");
    private static readonly MovementRules _rules = new();

    private static MotorInput Input(float x = 0f, float y = 0f, InputButtons buttons = InputButtons.None, float yaw = 0f, float pitch = 0f) =>
        new(x, y, yaw, pitch, buttons);

    private static IdealSphereBody Standing() => IdealSphereBody.OnSurface(_planet, _rules, Vector3.UnitX);

    private static IdealSphereBody InAir(float altitude) =>
        new(_planet, _rules, Vector3.UnitX * (_planet.RadiusMeters + altitude), onFloor: false, MovementMode.Falling);

    private static float Altitude(Vector3 feet) => feet.Length() - _planet.RadiusMeters;

    private static float Radial(IdealSphereBody body) => Vector3.Dot(body.Velocity, TerrainMath.UpAt(body.Feet));

    private static float TangentialSpeed(IdealSphereBody body)
    {
        var up = TerrainMath.UpAt(body.Feet);
        return (body.Velocity - (up * Vector3.Dot(body.Velocity, up))).Length();
    }

    [Fact]
    public void CharacterMotor_GravityInsideAtmosphere_TowardCenter()
    {
        var feet = Vector3.Normalize(new Vector3(1f, 2f, -3f)) * (_planet.RadiusMeters + 999f);

        var gravity = CharacterMotor.GravityAt(feet, _planet);

        Assert.True((gravity - (-TerrainMath.UpAt(feet) * 9.81f)).Length() < 1e-4f, $"gravity {gravity}");
    }

    [Fact]
    public void CharacterMotor_GravityOutsideAtmosphere_Zero()
    {
        var feet = Vector3.Normalize(new Vector3(1f, 2f, -3f)) * (_planet.RadiusMeters + 1001f);

        Assert.Equal(Vector3.Zero, CharacterMotor.GravityAt(feet, _planet));
    }

    [Fact]
    public void CharacterMotor_StandingStill_StaysOnSurfaceWithoutJitter()
    {
        var body = Standing();

        for (var i = 0; i < 600; i++)
        {
            var output = body.Step(Input(), devFlightAllowed: false, Dt);

            Assert.Equal(MovementMode.Walking, output.Mode);
            Assert.True(body.OnFloor);
            Assert.True(MathF.Abs(Altitude(body.Feet)) <= 1e-3f, $"tick {i}: altitude {Altitude(body.Feet)}");
            Assert.True(body.Velocity.Length() <= 1e-3f, $"tick {i}: velocity {body.Velocity}");
        }
    }

    [Fact]
    public void CharacterMotor_WalkDiagonal_SpeedCappedToWalk()
    {
        var body = Standing();

        for (var i = 0; i < 120; i++)
        {
            var output = body.Step(Input(1f, 1f), devFlightAllowed: false, Dt);

            Assert.Equal(MovementMode.Walking, output.Mode);
            Assert.True(TangentialSpeed(body) <= _rules.WalkSpeed + 1e-4f, $"tick {i}: speed {TangentialSpeed(body)}");
        }

        Assert.InRange(TangentialSpeed(body), _rules.WalkSpeed - 1e-3f, _rules.WalkSpeed + 1e-4f);
    }

    [Fact]
    public void CharacterMotor_Sprint_CappedToSprint()
    {
        var body = Standing();

        for (var i = 0; i < 120; i++)
        {
            var output = body.Step(Input(1f, 1f, InputButtons.Sprint), devFlightAllowed: false, Dt);

            Assert.True(output.Mode is MovementMode.Walking or MovementMode.Sprinting);
            Assert.True(TangentialSpeed(body) <= _rules.SprintSpeed + 1e-4f, $"tick {i}: speed {TangentialSpeed(body)}");
        }

        Assert.InRange(TangentialSpeed(body), _rules.SprintSpeed - 1e-3f, _rules.SprintSpeed + 1e-4f);
        Assert.Equal(MovementMode.Sprinting, body.Mode);
    }

    [Fact]
    public void CharacterMotor_SprintWithoutDirection_IsWalkingNotSprinting()
    {
        var body = Standing();

        var output = body.Step(Input(0f, 0f, InputButtons.Sprint), devFlightAllowed: false, Dt);

        Assert.Equal(MovementMode.Walking, output.Mode);
    }

    [Fact]
    public void CharacterMotor_ReleasingDirection_StopsOnTheGround()
    {
        var body = Standing();
        for (var i = 0; i < 60; i++)
        {
            body.Step(Input(0f, 1f, InputButtons.Sprint), devFlightAllowed: false, Dt);
        }

        // 7.5 m/s at 50 m/s² stops in 0.15 s.
        for (var i = 0; i < 12; i++)
        {
            body.Step(Input(), devFlightAllowed: false, Dt);
        }

        Assert.True(TangentialSpeed(body) <= 1e-3f, $"speed {TangentialSpeed(body)}");
    }

    [Fact]
    public void CharacterMotor_ForwardAtYawZero_MovesAlongNorth()
    {
        var body = Standing();

        for (var i = 0; i < 30; i++)
        {
            body.Step(Input(0f, 1f), devFlightAllowed: false, Dt);
        }

        var up = TerrainMath.UpAt(body.Feet);
        var north = SurfaceFrame.Reference(up);
        Assert.True(Vector3.Dot(Vector3.Normalize(body.Velocity), north) > 0.9999f);
    }

    [Fact]
    public void CharacterMotor_StrafeRight_MovesToTheRightOfForward()
    {
        var body = Standing();

        for (var i = 0; i < 30; i++)
        {
            body.Step(Input(1f, 0f), devFlightAllowed: false, Dt);
        }

        var up = TerrainMath.UpAt(body.Feet);
        var forward = SurfaceFrame.Reference(up);
        var right = Vector3.Cross(forward, up);
        Assert.True(Vector3.Dot(Vector3.Normalize(body.Velocity), right) > 0.9999f);
    }

    [Fact]
    public void CharacterMotor_JumpHeld_IsLevelTriggered_NoGainInAirAndReJumpOneTickAfterLanding()
    {
        var body = Standing();
        var airborne = false;
        var landed = false;

        for (var tick = 0; tick < 400; tick++)
        {
            var radialBefore = Radial(body);
            var wasOnFloor = body.OnFloor;

            // Jump stays held for the whole run, including the landing.
            var output = body.Step(Input(buttons: InputButtons.Jump), devFlightAllowed: false, Dt);

            if (landed)
            {
                // Exactly one tick after landing the held Jump launches again: Falling -> Jumping.
                Assert.Equal(MovementMode.Jumping, output.Mode);
                Assert.InRange(Radial(body), _rules.JumpSpeed - (_planet.Gravity * Dt) - 1e-3f, _rules.JumpSpeed + 1e-3f);
                return;
            }

            if (tick == 0)
            {
                Assert.True(wasOnFloor);
                Assert.Equal(MovementMode.Jumping, output.Mode);
                Assert.InRange(Radial(body), _rules.JumpSpeed - (_planet.Gravity * Dt) - 1e-3f, _rules.JumpSpeed + 1e-3f);
            }
            else if (!wasOnFloor && !body.OnFloor)
            {
                // In the air a held Jump adds nothing: the radial speed only falls with gravity.
                airborne = true;
                Assert.True(Radial(body) <= radialBefore + 1e-4f, $"tick {tick}: radial {radialBefore} -> {Radial(body)}");
            }
            else if (airborne && body.OnFloor)
            {
                // The landing tick itself is still decided from the airborne start of the tick.
                Assert.Equal(MovementMode.Falling, output.Mode);
                landed = true;
            }
        }

        Assert.Fail("The body never landed and jumped again within 400 ticks.");
    }

    [Fact]
    public void CharacterMotor_JumpHeight_MatchesBallisticEstimate()
    {
        var body = Standing();
        var peak = 0f;

        for (var tick = 0; tick < 200; tick++)
        {
            body.Step(Input(buttons: tick == 0 ? InputButtons.Jump : InputButtons.None), devFlightAllowed: false, Dt);
            peak = MathF.Max(peak, Altitude(body.Feet));
        }

        var expected = _rules.JumpSpeed * _rules.JumpSpeed / (2f * _planet.Gravity);
        Assert.InRange(peak, expected - 0.1f, expected + 0.1f);
    }

    [Fact]
    public void CharacterMotor_Fall_ClampedToMaxFallSpeedAndLands()
    {
        var body = InAir(500f);
        var fastest = 0f;

        for (var tick = 0; tick < 2000 && !body.OnFloor; tick++)
        {
            body.Step(Input(), devFlightAllowed: false, Dt);
            fastest = MathF.Max(fastest, -Radial(body));
        }

        Assert.True(body.OnFloor);
        Assert.InRange(fastest, _rules.MaxFallSpeed - 1e-3f, _rules.MaxFallSpeed + 1e-3f);
        body.Step(Input(), devFlightAllowed: false, Dt);
        Assert.Equal(MovementMode.Walking, body.Mode);
    }

    [Fact]
    public void CharacterMotor_AboveAtmosphere_NoGravity()
    {
        var body = InAir(_planet.AtmosphereHeightMeters + 500f);
        var start = body.Feet;

        for (var tick = 0; tick < 60; tick++)
        {
            body.Step(Input(), devFlightAllowed: false, Dt);
        }

        Assert.Equal(start, body.Feet);
        Assert.Equal(Vector3.Zero, body.Velocity);
    }

    [Fact]
    public void CharacterMotor_Frozen_DoesNotMoveAndStaysFrozen()
    {
        var body = new IdealSphereBody(_planet, _rules, Vector3.UnitX * (_planet.RadiusMeters + 5f), onFloor: false, MovementMode.Frozen);
        var start = body.Feet;

        for (var tick = 0; tick < 30; tick++)
        {
            var output = body.Step(Input(1f, 1f, InputButtons.Jump), devFlightAllowed: true, Dt);

            Assert.Equal(MovementMode.Frozen, output.Mode);
            Assert.Equal(Vector3.Zero, output.DesiredVelocity);
        }

        Assert.Equal(start, body.Feet);
    }

    [Fact]
    public void CharacterMotor_BodyRotation_IsFromYawOfTheCurrentUp()
    {
        var body = IdealSphereBody.OnSurface(_planet, _rules, new Vector3(1f, 1f, 1f));
        var up = TerrainMath.UpAt(body.Feet);

        var output = body.Step(Input(yaw: 0.7f), devFlightAllowed: false, Dt);

        var expected = SurfaceFrame.FromYaw(up, 0.7f);
        Assert.True((output.Up - up).Length() < 1e-6f);
        Assert.True(Quaternion.Dot(output.BodyRotation, expected) is > 0.999999f or < -0.999999f);
        Assert.True(MathF.Abs(output.BodyRotation.Length() - 1f) < 1e-6f);
    }

    [Fact]
    public void CharacterMotor_DevFly_IgnoredWhenNotAllowed()
    {
        var plain = Standing();
        var flying = Standing();

        for (var tick = 0; tick < 60; tick++)
        {
            var a = plain.Step(Input(0f, 1f, InputButtons.Jump), devFlightAllowed: false, Dt);
            var b = flying.Step(Input(0f, 1f, InputButtons.Jump | InputButtons.DevFly), devFlightAllowed: false, Dt);

            Assert.Equal(a, b);
            Assert.NotEqual(MovementMode.DevFlying, b.Mode);
        }

        Assert.Equal(plain.Feet, flying.Feet);
    }

    [Fact]
    public void CharacterMotor_DevFly_Allowed_NoGravityAndRadiusClamped()
    {
        var hover = InAir(100f);
        var start = hover.Feet;
        for (var tick = 0; tick < 120; tick++)
        {
            var output = hover.Step(Input(buttons: InputButtons.DevFly), devFlightAllowed: true, Dt);

            Assert.Equal(MovementMode.DevFlying, output.Mode);
        }

        Assert.Equal(start, hover.Feet);

        // Sprinting straight up: 150 m/s reaches the clamp of 2.25 R well within 60 s and never passes it.
        var climber = Standing();
        var limit = _rules.MaxDevFlightRadiusFactor * _planet.RadiusMeters;
        for (var tick = 0; tick < 3600; tick++)
        {
            climber.Step(Input(buttons: InputButtons.DevFly | InputButtons.Jump | InputButtons.Sprint), devFlightAllowed: true, Dt);

            Assert.True(climber.Feet.Length() <= limit + 1e-2f, $"tick {tick}: radius {climber.Feet.Length()}");
        }

        Assert.InRange(climber.Feet.Length(), limit - 1f, limit + 1e-2f);
        Assert.Equal(MovementMode.DevFlying, climber.Mode);
    }

    [Fact]
    public void CharacterMotor_DevFly_FollowsLookDirectionAtDevSpeed()
    {
        const float pitch = 0.5f;
        var body = InAir(100f);
        var up = TerrainMath.UpAt(body.Feet);

        var output = body.Step(Input(0f, 1f, InputButtons.DevFly, yaw: 0f, pitch: pitch), devFlightAllowed: true, Dt);

        Assert.Equal(_rules.DevFlySpeed, output.DesiredVelocity.Length(), 1e-3f);
        var direction = Vector3.Normalize(output.DesiredVelocity);
        Assert.Equal(MathF.Sin(pitch), Vector3.Dot(direction, up), 1e-4f);
        Assert.True(Vector3.Dot(direction, SurfaceFrame.Reference(up)) > 0f);
    }

    [Fact]
    public void CharacterMotor_DevFly_CrouchDescends()
    {
        var body = InAir(100f);

        body.Step(Input(buttons: InputButtons.DevFly | InputButtons.Crouch), devFlightAllowed: true, Dt);

        Assert.Equal(-_rules.DevFlySpeed, Radial(body), 1e-3f);
    }

    [Fact]
    public void CharacterMotor_WalkGreatCircleThroughBothPoles_NoJumps()
    {
        // The client keeps a continuous orientation and derives the yaw of every input from it; no mouse movement here.
        const int ticks = 201_000;
        var circumference = 2f * MathF.PI * _planet.RadiusMeters;
        var body = Standing();
        var start = body.Feet;
        var orientation = SurfaceFrame.FromYaw(TerrainMath.UpAt(body.Feet), 0f);
        var previousForward = Vector3.Zero;
        var previousRadius = body.Feet.Length();
        var worstTurnDegrees = 0f;
        var worstUpError = 0f;
        var worstHeightStep = 0f;

        for (var tick = 0; tick < ticks; tick++)
        {
            var up = TerrainMath.UpAt(body.Feet);
            orientation = SurfaceFrame.TransportToUp(orientation, up);
            var yaw = SurfaceFrame.YawOf(orientation, up);

            var output = body.Step(Input(0f, 1f, InputButtons.Sprint, yaw), devFlightAllowed: false, Dt);

            var forward = Vector3.Transform(-Vector3.UnitZ, output.BodyRotation);
            if (tick > 0)
            {
                var turn = MathF.Atan2(Vector3.Cross(previousForward, forward).Length(), Vector3.Dot(previousForward, forward));
                worstTurnDegrees = MathF.Max(worstTurnDegrees, turn * 180f / MathF.PI);
                worstHeightStep = MathF.Max(worstHeightStep, MathF.Abs(body.Feet.Length() - previousRadius));
            }

            worstUpError = MathF.Max(worstUpError, (Vector3.Transform(Vector3.UnitY, output.BodyRotation) - up).Length());
            previousForward = forward;
            previousRadius = body.Feet.Length();
        }

        Assert.True((body.Feet - start).Length() <= 0.005f * circumference, $"ended {(body.Feet - start).Length()} m from the start");
        Assert.True(worstUpError <= 1e-4f, $"body up differs from surface up by {worstUpError}");
        Assert.True(worstTurnDegrees <= 0.01f, $"body turned {worstTurnDegrees} degrees in one tick");
        Assert.True(worstHeightStep <= 0.01f, $"height changed {worstHeightStep} m in one tick");
    }

    [Fact]
    public void CharacterMotor_Step_DoesNotAllocate()
    {
        var body = Standing();
        var inputs = new[]
        {
            Input(0f, 1f, InputButtons.Sprint, yaw: 0.3f),
            Input(1f, 0f, InputButtons.Jump, yaw: -1f),
            Input(),
            Input(0f, 1f, InputButtons.DevFly | InputButtons.Jump, yaw: 2f, pitch: 0.4f),
            Input(0.5f, -0.5f, InputButtons.None, yaw: 3f),
        };
        for (var i = 0; i < 500; i++)
        {
            body.Step(inputs[i % inputs.Length], devFlightAllowed: true, Dt);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            body.Step(inputs[i % inputs.Length], devFlightAllowed: true, Dt);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
