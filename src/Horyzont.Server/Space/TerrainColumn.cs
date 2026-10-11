using System.Numerics;

namespace Horyzont.Server.Space;

/// <summary>
/// The column of terrain under a ship (README Y11, K5-22 decision 5, K5-24 decision 17): the readiness query of flight and the
/// collision viewer of the ship cover the same sphere. <c>d = min(altitude, 2 s · speed + 64 m)</c>; the centre lies on the radius
/// under the ship at <c>max(R + MaxRelief, r − d/2)</c>; the query radius is the ship radius + <c>d/2</c>, the viewer adds
/// <see cref="ViewerMarginMeters"/>.
/// </summary>
/// <param name="Center">Centre of the query sphere in the planet frame.</param>
/// <param name="Radius">Radius of the query sphere (ship radius + d/2).</param>
/// <param name="Depth">The look-ahead depth d.</param>
public readonly record struct TerrainColumn(Vector3 Center, float Radius, float Depth)
{
    /// <summary>Seconds of look-ahead at the current speed.</summary>
    public const float LookaheadSeconds = 2f;

    /// <summary>Look-ahead added to the speed term, in metres.</summary>
    public const float MarginMeters = 64f;

    /// <summary>Extra view distance of the collision viewer of a ship over the query radius, in metres.</summary>
    public const float ViewerMarginMeters = 64f;

    /// <summary>Gets the view distance of the collision viewer that covers this column (radius + <see cref="ViewerMarginMeters"/>).</summary>
    public float ViewerDistance => Radius + ViewerMarginMeters;

    /// <summary>Computes the column under a ship.</summary>
    /// <param name="shipCenter">Centre of the bounding sphere of the ship in the planet frame.</param>
    /// <param name="shipRadius">Radius of the bounding sphere of the ship.</param>
    /// <param name="speed">Speed of the ship in m/s (non-negative).</param>
    /// <param name="altitude">Altitude of the ship over the ground used by the caller (K5-24: <c>FlightEnvironment.Altitude</c>; ≥ 0).</param>
    /// <param name="planetRadius">Surface radius R.</param>
    /// <param name="maxReliefMeters">Highest relief over R of the generator (<c>PlanetGeneratorV1.MaxReliefMeters</c>).</param>
    /// <returns>The column.</returns>
    public static TerrainColumn UnderShip(Vector3 shipCenter, float shipRadius, float speed, float altitude, float planetRadius, float maxReliefMeters)
    {
        var depth = MathF.Min(MathF.Max(altitude, 0f), (LookaheadSeconds * MathF.Max(speed, 0f)) + MarginMeters);
        var r = shipCenter.Length();
        var up = r > 1e-3f ? shipCenter / r : Vector3.UnitY;
        var height = MathF.Max(planetRadius + maxReliefMeters, r - (depth / 2f));
        return new TerrainColumn(up * height, MathF.Max(shipRadius, 0f) + (depth / 2f), depth);
    }
}
