using System.Numerics;

using Horyzont.Core.Motion;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Testing.Motion;

/// <summary>
/// Integrator for motor tests: a point body on a perfect sphere of the planet radius instead of the engine's MoveAndSlide.
/// It applies <see cref="MotorOutput.DesiredVelocity"/> for one tick, keeps the body out of the sphere, sticks it to the floor within
/// <see cref="MovementRules.FloorSnapLength"/> while it does not move away, and carries the velocity along the surface.
/// </summary>
public sealed class IdealSphereBody
{
    /// <summary>Radial speed above which the body leaves the floor instead of sticking to it (m/s); float noise stays below.</summary>
    private const float MovingAwaySpeed = 1e-3f;

    private readonly PlanetParams _planet;
    private readonly MovementRules _rules;

    public IdealSphereBody(PlanetParams planet, MovementRules rules, Vector3 feet, bool onFloor, MovementMode mode)
    {
        _planet = planet;
        _rules = rules;
        Feet = feet;
        OnFloor = onFloor;
        Mode = mode;
    }

    public Vector3 Feet { get; private set; }

    public Vector3 Velocity { get; private set; }

    public bool OnFloor { get; private set; }

    public MovementMode Mode { get; private set; }

    /// <summary>Body standing on the surface at <paramref name="direction"/> (any length), mode Walking.</summary>
    public static IdealSphereBody OnSurface(PlanetParams planet, MovementRules rules, Vector3 direction) =>
        new(planet, rules, Vector3.Normalize(direction) * planet.RadiusMeters, onFloor: true, MovementMode.Walking);

    public MotorContext Context(bool devFlightAllowed) => new(Feet, Velocity, OnFloor, Mode, _planet, devFlightAllowed);

    /// <summary>Runs the motor and applies its output for one tick.</summary>
    public MotorOutput Step(in MotorInput input, bool devFlightAllowed, float dt)
    {
        var context = Context(devFlightAllowed);
        var output = CharacterMotor.Step(input, context, _rules, dt);
        Apply(output, dt);
        return output;
    }

    /// <summary>Applies a motor output for one tick.</summary>
    public void Apply(in MotorOutput output, float dt)
    {
        Mode = output.Mode;
        if (output.Mode == MovementMode.Frozen)
        {
            Velocity = Vector3.Zero;
            return;
        }

        var velocity = output.DesiredVelocity;
        var moved = Feet + (velocity * dt);
        var surface = _planet.RadiusMeters;
        var distance = moved.Length();
        var movingAway = Vector3.Dot(velocity, output.Up) > MovingAwaySpeed;

        var onFloor = false;
        if (distance <= surface || (OnFloor && !movingAway && distance - surface <= _rules.FloorSnapLength))
        {
            moved *= surface / distance;
            onFloor = true;
        }

        var newUp = TerrainMath.UpAt(moved);
        velocity = Carry(velocity, output.Up, newUp);
        if (onFloor)
        {
            // Floor contact removes the velocity into the ground, as the engine's slide does.
            var radial = Vector3.Dot(velocity, newUp);
            if (radial < 0f)
            {
                velocity -= newUp * radial;
            }
        }

        Feet = moved;
        Velocity = velocity;
        OnFloor = onFloor;
    }

    /// <summary>Rotates a velocity with the surface: the minimal rotation from the old up to the new one.</summary>
    private static Vector3 Carry(Vector3 velocity, Vector3 oldUp, Vector3 newUp)
    {
        var axis = Vector3.Cross(oldUp, newUp);
        var w = 1f + Vector3.Dot(oldUp, newUp);
        if (w < 1e-6f)
        {
            return velocity;
        }

        var turn = Quaternion.Normalize(new Quaternion(axis, w));
        return Vector3.Transform(velocity, turn);
    }
}
