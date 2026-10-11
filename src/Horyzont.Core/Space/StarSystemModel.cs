using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World;

namespace Horyzont.Core.Space;

/// <summary>
/// Immutable view of a star system joined with the planets of its bodies (K5-21 decision 15). Queries are allocation free and
/// meant for hot paths: zone lookup and gravity in the system frame.
/// </summary>
public sealed class StarSystemModel
{
    private readonly BodyInfo[] _bodies;

    /// <summary>Initializes a new instance of the <see cref="StarSystemModel"/> class.</summary>
    /// <param name="system">The star system.</param>
    /// <param name="planets">Planets containing every body of the system (for example <see cref="PlanetRegistry.All"/>).</param>
    /// <exception cref="InvalidDataException">A body has no planet in <paramref name="planets"/>.</exception>
    public StarSystemModel(StarSystemParams system, IReadOnlyList<PlanetParams> planets)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(planets);
        System = system;
        _bodies = new BodyInfo[system.Bodies.Count];
        for (var i = 0; i < _bodies.Length; i++)
        {
            var body = system.Bodies[i];
            var planet = Find(planets, body.Planet)
                ?? throw new InvalidDataException($"Star system {system.System.Value}: planet {body.Planet.Value} of body {body.BodyIndex} is missing.");
            _bodies[i] = new BodyInfo(body.Planet, body.BodyIndex, body.Pose.Position, planet.RadiusMeters, planet.Gravity);
        }
    }

    /// <summary>Gets the star system.</summary>
    public StarSystemParams System { get; }

    /// <summary>Gets the number of bodies.</summary>
    public int BodyCount => _bodies.Length;

    /// <summary>Gets a body by its position in <see cref="StarSystemParams.Bodies"/>.</summary>
    /// <param name="index">Index from 0 to <see cref="BodyCount"/> − 1.</param>
    /// <returns>The body.</returns>
    public BodyInfo GetBody(int index) => _bodies[index];

    /// <summary>Looks up the body of a planet.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="body">Receives the body.</param>
    /// <returns>True when the planet is a body of the system.</returns>
    public bool TryGetBody(PlanetId planet, out BodyInfo body)
    {
        for (var i = 0; i < _bodies.Length; i++)
        {
            if (_bodies[i].Planet == planet)
            {
                body = _bodies[i];
                return true;
            }
        }

        body = default;
        return false;
    }

    /// <summary>
    /// Finds the body whose zone of influence (distance ≤ <see cref="StarSystemRules.ZoneRadiusFactor"/> R) contains a point.
    /// Zones never overlap (separation by construction), so at most one body matches.
    /// </summary>
    /// <param name="systemPosition">Point in the system frame.</param>
    /// <param name="bodyIndex">Receives the index of the body for <see cref="GetBody"/>.</param>
    /// <param name="distance">Receives the distance from the centre of the body.</param>
    /// <returns>True when the point lies in a zone.</returns>
    public bool TryFindZone(in Vector3D systemPosition, out int bodyIndex, out double distance)
    {
        for (var i = 0; i < _bodies.Length; i++)
        {
            var d = Vector3D.Distance(systemPosition, _bodies[i].Position);
            if (d <= StarSystemRules.ZoneRadiusFactor * _bodies[i].RadiusMeters)
            {
                bodyIndex = i;
                distance = d;
                return true;
            }
        }

        bodyIndex = -1;
        distance = 0;
        return false;
    }

    /// <summary>Gravity in the system frame: the gravity of the body whose zone contains the point, otherwise zero.</summary>
    /// <param name="systemPosition">Point in the system frame.</param>
    /// <returns>The acceleration in m/s².</returns>
    public Vector3D GravityAt(in Vector3D systemPosition)
    {
        if (!TryFindZone(systemPosition, out var index, out _))
        {
            return Vector3D.Zero;
        }

        var body = _bodies[index];
        return StarSystemMath.Towards(systemPosition - body.Position, body.RadiusMeters, body.SurfaceGravity);
    }

    private static PlanetParams? Find(IReadOnlyList<PlanetParams> planets, PlanetId id)
    {
        for (var i = 0; i < planets.Count; i++)
        {
            if (planets[i].Id == id)
            {
                return planets[i];
            }
        }

        return null;
    }
}
