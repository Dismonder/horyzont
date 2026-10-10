using Horyzont.Core.Ids;

using NumQuaternion = System.Numerics.Quaternion;
using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Frames;

/// <summary>
/// The only conversion between Godot (<c>double</c>) and the frame of a planet (<c>System.Numerics</c>, <c>float</c>, metres; ZASADY
/// point 2). The terrain node of a planet sits at the Godot origin with an identity basis (K2-09), so the frame of every planet
/// is the Godot world today; keeping the conversion here means a later planet placement changes this file only. Every conversion
/// checks for finite values and for the range <c>float</c> can hold in the frame.
/// </summary>
public static class PlanetFrames
{
    /// <summary>Largest coordinate accepted in a planet frame (metres); the dev flight is clamped well inside this.</summary>
    public const float MaxCoordinate = 100_000f;

    /// <summary>Converts a Godot position (world space) to the frame of a planet.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="position">Godot position.</param>
    /// <returns>The planet-frame position.</returns>
    /// <exception cref="ArgumentException">The planet is unknown or the value is not finite or out of range.</exception>
    public static NumVector3 ToPlanetLocal(PlanetId planet, Godot.Vector3 position)
    {
        RequireKnown(planet);
        return new NumVector3(Coordinate(position.X), Coordinate(position.Y), Coordinate(position.Z));
    }

    /// <summary>Converts a position in the frame of a planet to Godot (world space).</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="position">Planet-frame position.</param>
    /// <returns>The Godot position.</returns>
    /// <exception cref="ArgumentException">The planet is unknown or the value is not finite or out of range.</exception>
    public static Godot.Vector3 ToGodot(PlanetId planet, NumVector3 position)
    {
        RequireKnown(planet);
        return new Godot.Vector3(Coordinate(position.X), Coordinate(position.Y), Coordinate(position.Z));
    }

    /// <summary>Converts a direction or velocity (no offset) to the frame of a planet.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="vector">Godot vector.</param>
    /// <returns>The vector in the frame.</returns>
    /// <exception cref="ArgumentException">The planet is unknown or the value is not finite.</exception>
    public static NumVector3 ToPlanetLocalDirection(PlanetId planet, Godot.Vector3 vector)
    {
        RequireKnown(planet);
        return new NumVector3(Finite(vector.X), Finite(vector.Y), Finite(vector.Z));
    }

    /// <summary>Converts a direction or velocity of the planet frame to Godot.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="vector">Frame vector.</param>
    /// <returns>The Godot vector.</returns>
    /// <exception cref="ArgumentException">The planet is unknown or the value is not finite.</exception>
    public static Godot.Vector3 ToGodotDirection(PlanetId planet, NumVector3 vector)
    {
        RequireKnown(planet);
        return new Godot.Vector3(Finite(vector.X), Finite(vector.Y), Finite(vector.Z));
    }

    /// <summary>Converts a Godot rotation to a normalized frame rotation.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="rotation">Godot rotation.</param>
    /// <returns>The normalized rotation.</returns>
    /// <exception cref="ArgumentException">The planet is unknown or the value is not finite.</exception>
    public static NumQuaternion ToNumerics(PlanetId planet, Godot.Quaternion rotation)
    {
        RequireKnown(planet);
        var normalized = rotation.Normalized();
        return NumQuaternion.Normalize(new NumQuaternion(Finite(normalized.X), Finite(normalized.Y), Finite(normalized.Z), Finite(normalized.W)));
    }

    /// <summary>Converts a Godot basis to a normalized frame rotation.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="basis">Godot basis (rotation; any scale is removed).</param>
    /// <returns>The normalized rotation.</returns>
    /// <exception cref="ArgumentException">The planet is unknown or the value is not finite.</exception>
    public static NumQuaternion ToNumerics(PlanetId planet, Godot.Basis basis) => ToNumerics(planet, basis.Orthonormalized().GetRotationQuaternion());

    /// <summary>Converts a frame rotation to a Godot quaternion.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="rotation">Frame rotation.</param>
    /// <returns>The normalized Godot quaternion.</returns>
    /// <exception cref="ArgumentException">The planet is unknown or the value is not finite.</exception>
    public static Godot.Quaternion ToGodot(PlanetId planet, NumQuaternion rotation)
    {
        RequireKnown(planet);
        return new Godot.Quaternion(Finite(rotation.X), Finite(rotation.Y), Finite(rotation.Z), Finite(rotation.W)).Normalized();
    }

    /// <summary>Converts a frame rotation to a Godot basis.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="rotation">Frame rotation.</param>
    /// <returns>The Godot basis.</returns>
    /// <exception cref="ArgumentException">The planet is unknown or the value is not finite.</exception>
    public static Godot.Basis ToGodotBasis(PlanetId planet, NumQuaternion rotation) => new(ToGodot(planet, rotation));

    private static void RequireKnown(PlanetId planet)
    {
        if (planet.Value == 0)
        {
            throw new ArgumentException("Planet id 0 is reserved and has no frame.", nameof(planet));
        }
    }

    private static float Finite(double value)
    {
        var narrowed = (float)value;
        return float.IsFinite(narrowed) ? narrowed : throw new ArgumentException($"Non-finite value {value} in a frame conversion.");
    }

    private static float Coordinate(double value)
    {
        var narrowed = Finite(value);
        return MathF.Abs(narrowed) <= MaxCoordinate ? narrowed : throw new ArgumentException($"Coordinate {value} is beyond the {MaxCoordinate} m of a planet frame.");
    }
}
