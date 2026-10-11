using Horyzont.Core.Frames;
using Horyzont.Core.Space;
using Horyzont.Core.World;

namespace Horyzont.Core.Ships.Flight;

/// <summary>
/// Builds <see cref="FlightEnvironment"/> in a planet frame and in the system frame (K5-24 decision 5). Pure and allocation free;
/// the gate, the launch block and the horizontal cap default to ready, none and infinite — the caller sets them with <c>with</c>.
/// </summary>
public static class FlightEnvironments
{
    /// <summary>Fraction of the atmosphere thickness at its top over which the drag fades to zero.</summary>
    public const double DragFadeFraction = 0.2;

    /// <summary>Surroundings in the frame of <paramref name="planet"/> (origin at its centre).</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="bodyIndex">Body index of the planet (0 without a star system).</param>
    /// <param name="local">Position of the ship in the planet frame.</param>
    /// <param name="rules">Rules.</param>
    /// <returns>The environment.</returns>
    public static FlightEnvironment InPlanet(PlanetParams planet, int bodyIndex, in Vector3D local, FlightRules rules)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(rules);
        var r = local.Length();
        double radius = planet.RadiusMeters;
        double height = planet.AtmosphereHeightMeters;
        var drag = 0f;
        if (height > 0 && r < radius + height)
        {
            var fade = Math.Clamp((radius + height - r) / (DragFadeFraction * height), 0, 1);
            drag = (float)(rules.DragOf(bodyIndex) * fade);
        }

        return new FlightEnvironment
        {
            Gravity = StarSystemMath.GravityInPlanet(local, planet),
            Up = r > 1e-9 ? local / r : new Vector3D(0, 1, 0),
            Altitude = Math.Max(0, r - radius - rules.SurfaceClearanceMeters),
            RadiusOfBody = radius,
            DistanceFromBody = r,
            DragPerSecond = drag,
            HorizontalSpeedCap = double.PositiveInfinity,
        };
    }

    /// <summary>Surroundings in the system frame: the nearest body over its surface, gravity of the zone, no drag.</summary>
    /// <param name="system">The star system.</param>
    /// <param name="position">Position of the ship in the system frame.</param>
    /// <param name="rules">Rules.</param>
    /// <returns>The environment.</returns>
    public static FlightEnvironment InSystem(StarSystemModel system, in Vector3D position, FlightRules rules)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(rules);
        var best = 0;
        var bestHeight = double.PositiveInfinity;
        for (var i = 0; i < system.BodyCount; i++)
        {
            var body = system.GetBody(i);
            var height = Vector3D.Distance(position, body.Position) - body.RadiusMeters;
            if (height < bestHeight)
            {
                bestHeight = height;
                best = i;
            }
        }

        var nearest = system.GetBody(best);
        var offset = position - nearest.Position;
        var r = offset.Length();
        return new FlightEnvironment
        {
            Gravity = system.GravityAt(position),
            Up = r > 1e-9 ? offset / r : new Vector3D(0, 1, 0),
            Altitude = Math.Max(0, r - nearest.RadiusMeters - rules.SurfaceClearanceMeters),
            RadiusOfBody = nearest.RadiusMeters,
            DistanceFromBody = r,
            InSystemFrame = true,
            SystemDistance = position.Length(),
            HorizontalSpeedCap = double.PositiveInfinity,
        };
    }
}
