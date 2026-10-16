using System.Numerics;

using Horyzont.Core.Props;

namespace Horyzont.Client.Props;

/// <summary>
/// How far from the centre of its region (taken at the nominal radius of the planet) a prop can lie, which decides what the region filter of
/// the renderer may skip when the picker asks for the props around the eye (K6-31 review fix).
/// </summary>
public static class PropRegionReach
{
    /// <summary>
    /// The half of the 8 x 8 cells of a region plus the largest scale of a tall tree: a prop never lies farther than this from the centre of
    /// its region along the surface, metres.
    /// </summary>
    public const float AlongSurfaceMeters = 64f;

    /// <summary>
    /// Gets the distance a prop of a region can lie from the centre of that region in any direction, metres: along the surface plus the
    /// whole range of heights of the layout (the centre lies on the nominal radius, the anchor up to 300 m above or below it).
    /// </summary>
    public static float Meters => AlongSurfaceMeters + Math.Max(PropHeightLimitsV1.MaxAboveRadiusMeters, PropHeightLimitsV1.MaxBelowRadiusMeters);

    /// <summary>Tells whether a region may hold a prop whose anchor lies in a sphere.</summary>
    /// <param name="regionCentre">Centre of the region at the nominal radius of the planet, in the frame of the planet.</param>
    /// <param name="sphereCentre">Centre of the sphere in the same frame.</param>
    /// <param name="sphereRadius">Radius of the sphere in metres.</param>
    /// <returns>False only when no prop of the region can lie in the sphere.</returns>
    public static bool MayHold(Vector3 regionCentre, Vector3 sphereCentre, float sphereRadius) =>
        Vector3.Distance(regionCentre, sphereCentre) <= sphereRadius + Meters;
}
