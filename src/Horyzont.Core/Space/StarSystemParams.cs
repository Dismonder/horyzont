using Horyzont.Core.Frames;
using Horyzont.Core.Ids;

namespace Horyzont.Core.Space;

/// <summary>
/// The saved star system of a world (K5-21 decision 2; blob 10, message 32): only the placement of the bodies and the sun.
/// Radius, gravity and atmosphere of each body live in its <see cref="World.PlanetParams"/>. Immutable after creation.
/// </summary>
/// <param name="System">System id (1 in subprojects 1–6).</param>
/// <param name="LayoutVersion">The <see cref="StarSystemLayout"/> that generated the system.</param>
/// <param name="HomePlanet">The home planet; body 0, at the origin of the system frame.</param>
/// <param name="SunCycleSeconds">Length of one sun cycle (day) in seconds.</param>
/// <param name="Bodies">Bodies in <see cref="BodyPlacement.BodyIndex"/> order, 1..8 of them.</param>
public sealed record StarSystemParams(
    SystemId System,
    byte LayoutVersion,
    PlanetId HomePlanet,
    float SunCycleSeconds,
    IReadOnlyList<BodyPlacement> Bodies)
{
    /// <summary>Largest deviation of the length of a body rotation from 1.</summary>
    internal const double RotationTolerance = 1e-9;

    /// <inheritdoc />
    public bool Equals(StarSystemParams? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (System != other.System || LayoutVersion != other.LayoutVersion || HomePlanet != other.HomePlanet
            || BitConverter.SingleToInt32Bits(SunCycleSeconds) != BitConverter.SingleToInt32Bits(other.SunCycleSeconds)
            || Bodies.Count != other.Bodies.Count)
        {
            return false;
        }

        for (var i = 0; i < Bodies.Count; i++)
        {
            if (!Bodies[i].Equals(other.Bodies[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(System, LayoutVersion, HomePlanet, SunCycleSeconds, Bodies.Count);

    /// <summary>Checks the invariants of decision 2; used when a system is generated, decoded or added to a world.</summary>
    /// <exception cref="InvalidDataException">The system breaks an invariant.</exception>
    internal void Validate()
    {
        if (System.Value == 0)
        {
            throw new InvalidDataException("Star system id 0 is reserved.");
        }

        if (Bodies is null || Bodies.Count < 1 || Bodies.Count > StarSystemRules.MaxBodies)
        {
            throw new InvalidDataException($"Star system {System.Value}: 1..{StarSystemRules.MaxBodies} bodies required.");
        }

        if (!float.IsFinite(SunCycleSeconds) || SunCycleSeconds <= 0)
        {
            throw new InvalidDataException($"Star system {System.Value}: sun cycle out of range.");
        }

        for (var i = 0; i < Bodies.Count; i++)
        {
            var body = Bodies[i];
            if (body.Planet.Value == 0 || !body.Pose.Position.IsFinite || !body.Pose.Rotation.IsFinite
                || Math.Abs(body.Pose.Rotation.Length() - 1) > RotationTolerance)
            {
                throw new InvalidDataException($"Star system {System.Value}: body {body.BodyIndex} is invalid.");
            }

            if (i > 0 && Bodies[i - 1].BodyIndex >= body.BodyIndex)
            {
                throw new InvalidDataException($"Star system {System.Value}: bodies must be sorted by unique index.");
            }

            for (var j = 0; j < i; j++)
            {
                if (Bodies[j].Planet == body.Planet)
                {
                    throw new InvalidDataException($"Star system {System.Value}: planet {body.Planet.Value} appears twice.");
                }
            }
        }

        var home = Bodies[0];
        if (home.BodyIndex != 0 || home.Planet != HomePlanet || home.Pose.Position != Vector3D.Zero
            || home.Pose.Rotation != QuaternionD.Identity)
        {
            throw new InvalidDataException($"Star system {System.Value}: body 0 must be the home planet at the origin in the identity pose.");
        }
    }
}
