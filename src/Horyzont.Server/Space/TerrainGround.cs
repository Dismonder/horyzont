using System.Numerics;

namespace Horyzont.Server.Space;

/// <summary>
/// The one "ground in the column" test shared by the collision viewer of a ship and the readiness query (README Y11): the generated
/// ground under the centre of a sphere counts when it lies within the radial span of the sphere widened by <see cref="Core"/>; the
/// readiness then checks a box of <see cref="Core"/> around that ground point and the ground viewer stands on it. Pure.
/// </summary>
public static class TerrainGround
{
    /// <summary>Largest half-size of the box around the ground point (well inside the LOD 0 ring of a viewer standing on it).</summary>
    public const float CoreMeters = 40f;

    /// <summary>The half-size of the ground box of a query sphere: its radius, at most <see cref="CoreMeters"/>.</summary>
    /// <param name="radius">Radius of the sphere.</param>
    /// <returns>The half-size in metres.</returns>
    public static float Core(float radius) => MathF.Min(MathF.Max(radius, 0f), CoreMeters);

    /// <summary>The generated ground point under the centre of a sphere when it lies in the span of the sphere widened by the core.</summary>
    /// <param name="center">Centre of the sphere in the planet frame.</param>
    /// <param name="radius">Radius of the sphere.</param>
    /// <param name="groundRadius">Radius of the generated ground along the centre direction (NaN when unknown).</param>
    /// <param name="point">Receives the ground point.</param>
    /// <returns>False when the ground is unknown or outside the widened span.</returns>
    public static bool TryGroundPoint(Vector3 center, float radius, float groundRadius, out Vector3 point)
    {
        point = default;
        var distance = center.Length();
        if (distance <= 1f || !float.IsFinite(distance) || !float.IsFinite(groundRadius) || !float.IsFinite(radius))
        {
            return false;
        }

        var core = Core(radius);
        if (groundRadius < distance - radius - core || groundRadius > distance + radius + core)
        {
            return false;
        }

        point = center / distance * groundRadius;
        return true;
    }

    /// <summary>Where the ground viewer of a ship stands: on the ground point of its column, else at the centre of the column.</summary>
    /// <param name="column">The column.</param>
    /// <param name="groundRadius">Radius of the generated ground under the column (NaN when unknown).</param>
    /// <returns>The position in the planet frame.</returns>
    public static Vector3 ViewerPoint(in TerrainColumn column, float groundRadius)
        => TryGroundPoint(column.Center, column.Radius, groundRadius, out var point) ? point : column.Center;
}
