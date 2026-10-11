using System.Numerics;

using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Motion;

/// <summary>Pure movement step shared by the server and the client prediction. Allocation-free.</summary>
public static class CharacterMotor
{
    /// <summary>
    /// One tick of movement. <paramref name="dt"/> is 1 / <c>GameRules.TickRate</c>. The caller applies
    /// <see cref="MotorOutput.DesiredVelocity"/> (MoveAndSlide in the engine, an ideal integrator in tests).
    /// </summary>
    /// <param name="input">Validated input.</param>
    /// <param name="context">Body state before the step.</param>
    /// <param name="rules">Movement rules.</param>
    /// <param name="dt">Tick length in seconds.</param>
    /// <returns>The wanted velocity, up, body rotation and mode.</returns>
    public static MotorOutput Step(in MotorInput input, in MotorContext context, MovementRules rules, float dt)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var up = TerrainMath.UpAt(context.Feet);
        var rotation = SurfaceFrame.FromYaw(up, input.Yaw);
        if (context.PreviousMode == MovementMode.Frozen)
        {
            return new MotorOutput(Vector3.Zero, up, rotation, MovementMode.Frozen);
        }

        var forward = Vector3.Transform(-Vector3.UnitZ, rotation);
        var right = Vector3.Transform(Vector3.UnitX, rotation);
        if (context.DevFlightAllowed && (input.Buttons & InputButtons.DevFly) != 0)
        {
            return new MotorOutput(DevFlyVelocity(input, context, rules, dt, up, forward, right), up, rotation, MovementMode.DevFlying);
        }

        return Walk(input, context, rules, dt, up, forward, right, rotation);
    }

    /// <summary>
    /// Gravity: -UpAt(feet) * Planet.Gravity while |feet| is at most the radius plus the atmosphere height, otherwise zero.
    /// </summary>
    /// <param name="feet">Feet position in the planet frame.</param>
    /// <param name="planet">Planet.</param>
    /// <returns>The gravity vector in m/s².</returns>
    public static Vector3 GravityAt(Vector3 feet, PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        return feet.Length() <= planet.RadiusMeters + planet.AtmosphereHeightMeters ? -TerrainMath.UpAt(feet) * planet.Gravity : Vector3.Zero;
    }

    private static MotorOutput Walk(
        in MotorInput input, in MotorContext context, MovementRules rules, float dt, Vector3 up, Vector3 forward, Vector3 right,
        Quaternion rotation)
    {
        // Leaving developer flight drops its momentum.
        var velocity = context.PreviousMode == MovementMode.DevFlying ? Vector3.Zero : context.Velocity;
        var radial = Vector3.Dot(velocity, up);
        var horizontal = velocity - (up * radial);
        if (context.PreviousMode == MovementMode.Gliding && !context.OnFloor)
        {
            return Glide(input, context, rules, dt, up, forward, right, rotation, radial, horizontal);
        }

        var moveLength = MathF.Sqrt((input.MoveX * input.MoveX) + (input.MoveY * input.MoveY));
        var scale = moveLength > 1f ? 1f / moveLength : 1f;
        var hasDirection = moveLength > 0f;
        var sprint = hasDirection && (input.Buttons & InputButtons.Sprint) != 0;
        var wanted = ((right * (input.MoveX * scale)) + (forward * (input.MoveY * scale))) * (sprint ? rules.SprintSpeed : rules.WalkSpeed);

        if (context.OnFloor)
        {
            horizontal = MoveToward(horizontal, wanted, (hasDirection ? rules.GroundAcceleration : rules.GroundDeceleration) * dt);
        }
        else if (hasDirection)
        {
            horizontal = MoveToward(horizontal, wanted, rules.AirAcceleration * dt);
        }

        var jumped = false;
        if (context.OnFloor)
        {
            radial = 0f;
            if ((input.Buttons & InputButtons.Jump) != 0)
            {
                radial = rules.JumpSpeed;
                jumped = true;
            }
        }
        else
        {
            radial = Math.Max(radial + (Vector3.Dot(GravityAt(context.Feet, context.Planet), up) * dt), -rules.MaxFallSpeed);
        }

        MovementMode mode;
        if (context.OnFloor)
        {
            mode = jumped ? MovementMode.Jumping : sprint ? MovementMode.Sprinting : MovementMode.Walking;
        }
        else
        {
            mode = radial > 0f && context.PreviousMode == MovementMode.Jumping ? MovementMode.Jumping : MovementMode.Falling;
        }

        return new MotorOutput(horizontal + (up * radial), up, rotation, mode);
    }

    /// <summary>
    /// K5-25 decision 19: the suit glide after an ejection keeps the inherited velocity, damps its horizontal part, steers
    /// toward the walk speed and clamps the fall speed; it ends on the floor (walking).
    /// </summary>
    private static MotorOutput Glide(
        in MotorInput input, in MotorContext context, MovementRules rules, float dt, Vector3 up, Vector3 forward, Vector3 right,
        Quaternion rotation, float radial, Vector3 horizontal)
    {
        horizontal *= MathF.Max(0f, 1f - (rules.GlideHorizontalDamping * dt));
        var moveLength = MathF.Sqrt((input.MoveX * input.MoveX) + (input.MoveY * input.MoveY));
        if (moveLength > 0f)
        {
            var scale = moveLength > 1f ? 1f / moveLength : 1f;
            var wanted = ((right * (input.MoveX * scale)) + (forward * (input.MoveY * scale))) * rules.WalkSpeed;
            horizontal = MoveToward(horizontal, wanted, rules.AirAcceleration * dt);
        }

        radial = MathF.Max(radial + (Vector3.Dot(GravityAt(context.Feet, context.Planet), up) * dt), -rules.SuitGlideFallSpeed);
        return new MotorOutput(horizontal + (up * radial), up, rotation, MovementMode.Gliding);
    }

    private static Vector3 DevFlyVelocity(
        in MotorInput input, in MotorContext context, MovementRules rules, float dt, Vector3 up, Vector3 forward, Vector3 right)
    {
        var look = (forward * MathF.Cos(input.Pitch)) + (up * MathF.Sin(input.Pitch));
        var vertical = ((input.Buttons & InputButtons.Jump) != 0 ? 1f : 0f) - ((input.Buttons & InputButtons.Crouch) != 0 ? 1f : 0f);
        var wish = (look * input.MoveY) + (right * input.MoveX) + (up * vertical);
        var length = wish.Length();
        if (length > 1f)
        {
            wish /= length;
        }

        var velocity = wish * ((input.Buttons & InputButtons.Sprint) != 0 ? rules.DevFlySprintSpeed : rules.DevFlySpeed);

        // Stay within a multiple of the radius: float precision of the terrain and of the network fails further out.
        var limit = rules.MaxDevFlightRadiusFactor * context.Planet.RadiusMeters;
        var next = context.Feet + (velocity * dt);
        var distance = next.Length();
        if (distance > limit)
        {
            velocity = ((next * (limit / distance)) - context.Feet) / dt;
        }

        return velocity;
    }

    private static Vector3 MoveToward(Vector3 current, Vector3 target, float maxDelta)
    {
        var delta = target - current;
        var distance = delta.Length();
        return distance <= maxDelta || distance == 0f ? target : current + (delta * (maxDelta / distance));
    }
}
