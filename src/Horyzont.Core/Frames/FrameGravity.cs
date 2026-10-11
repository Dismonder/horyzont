using Horyzont.Core.Ids;
using Horyzont.Core.World;

namespace Horyzont.Core.Frames;

/// <summary>
/// Gravity of a reference frame (K5-20, decision 18): the radial gravity of the region planet (the rule of
/// <see cref="Motion.CharacterMotor.GravityAt"/>, in double) expressed in the frame; zero in a system region. Pure, no allocation.
/// </summary>
public static class FrameGravity
{
    /// <summary>Gravity of a planet at a position of its frame: radial inside the atmosphere, zero above it.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="position">Position in the planet frame.</param>
    /// <returns>The acceleration (m/s²) in the planet frame.</returns>
    public static Vector3D AtPlanet(PlanetParams planet, Vector3D position)
    {
        ArgumentNullException.ThrowIfNull(planet);
        var radius = position.Length();
        return radius > 0 && radius <= (double)planet.RadiusMeters + planet.AtmosphereHeightMeters
            ? position * (-planet.Gravity / radius)
            : Vector3D.Zero;
    }

    /// <summary>Gravity at a point of any frame, expressed in that frame.</summary>
    /// <param name="graph">The frame graph.</param>
    /// <param name="frame">The frame.</param>
    /// <param name="local">The point in the frame.</param>
    /// <param name="planets">Parameters of a planet; null when unknown.</param>
    /// <param name="gravity">Receives the acceleration in the frame.</param>
    /// <returns>False for an unknown frame or region planet.</returns>
    public static bool TryAt(FrameGraph graph, FrameRef frame, Vector3D local, Func<PlanetId, PlanetParams?> planets, out Vector3D gravity)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(planets);
        gravity = Vector3D.Zero;
        if (!graph.TryGetRegion(frame, out var region))
        {
            return false;
        }

        if (region.Kind != FrameKind.Planet)
        {
            return true;
        }

        if (planets(new PlanetId(region.Id)) is not { } planet)
        {
            return false;
        }

        var pose = PoseD.Identity;
        if (frame != region && !FrameGraph.TryGetPoseIn(graph.Source, frame, region, FrameGraph.MaxDepth, out pose))
        {
            return false;
        }

        gravity = FrameMath.InverseTransformDirection(pose, AtPlanet(planet, FrameMath.TransformPoint(pose, local)));
        return true;
    }
}
