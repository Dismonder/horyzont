using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Server.Space;

/// <summary>
/// The terrain readiness query of a ship (README Y11): its <see cref="TerrainColumn"/> (the ground under it) and, in the band near the
/// terrain, its <see cref="TerrainSweep"/>. <see cref="PlanetTerrainReadinessQueries.GetShipReadiness"/> answers it; the collision
/// viewers of the ship (K5-22 <c>ServerShipViewers</c>) cover the same places. Pure.
/// </summary>
/// <param name="Column">The column under the ship.</param>
/// <param name="Sweep">The sweep box (checked only when <paramref name="HasSweep"/>).</param>
/// <param name="HasSweep">Whether the ship is in the band where the sweep box must be ready.</param>
public readonly record struct ShipTerrainQuery(TerrainColumn Column, TerrainSweep Sweep, bool HasSweep)
{
    /// <summary>Computes the query of a ship.</summary>
    /// <param name="shipCenter">Centre of the bounding sphere of the ship in the planet frame.</param>
    /// <param name="shipRadius">Radius of the bounding sphere of the ship.</param>
    /// <param name="velocity">Velocity of the ship in the planet frame (m/s).</param>
    /// <param name="altitude">Altitude for <see cref="TerrainColumn.UnderShip"/> (≥ 0).</param>
    /// <param name="planetRadius">Surface radius R.</param>
    /// <param name="maxReliefMeters">Highest relief over R of the generator.</param>
    /// <returns>The query.</returns>
    public static ShipTerrainQuery Of(Vector3 shipCenter, float shipRadius, Vector3 velocity, float altitude, float planetRadius, float maxReliefMeters)
    {
        var speed = velocity.Length();
        var column = TerrainColumn.UnderShip(shipCenter, shipRadius, float.IsFinite(speed) ? speed : 0f, altitude, planetRadius, maxReliefMeters);
        var band = TerrainSweep.InBand(shipCenter, shipRadius, column.Depth, planetRadius, maxReliefMeters);
        return new ShipTerrainQuery(column, TerrainSweep.Ahead(shipCenter, shipRadius, velocity), band);
    }

    /// <summary>Whether the ship needs a second collision viewer at the sweep centre, given where its ground viewer stands.</summary>
    /// <param name="groundViewer">Position of the ground viewer (<see cref="TerrainGround.ViewerPoint"/>).</param>
    /// <returns>True in the band when the ground viewer is farther than <see cref="TerrainSweep.SingleViewerMeters"/> from the sweep centre.</returns>
    public bool NeedsSweepViewer(Vector3 groundViewer)
        => HasSweep && Vector3.Distance(groundViewer, Sweep.Center) > TerrainSweep.SingleViewerMeters;
}

/// <summary>Ship queries over <see cref="IPlanetTerrainReadiness"/> (README Y11).</summary>
public static class PlanetTerrainReadinessQueries
{
    /// <summary>
    /// Readiness for a ship: the column (ground) and, in the band, the sweep box (queried as the sphere of radius
    /// <see cref="TerrainSweep.HalfSize"/>, which <see cref="IPlanetTerrainReadiness"/> checks as a whole box); the worse of the two.
    /// </summary>
    /// <param name="readiness">The readiness source.</param>
    /// <param name="planet">The planet.</param>
    /// <param name="query">The query of the ship.</param>
    /// <returns>The readiness.</returns>
    public static PlanetTerrainReadiness GetShipReadiness(this IPlanetTerrainReadiness readiness, PlanetId planet, in ShipTerrainQuery query)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        var column = readiness.GetReadiness(planet, query.Column.Center, query.Column.Radius);
        return query.HasSweep ? Worse(column, readiness.GetReadiness(planet, query.Sweep.Center, query.Sweep.HalfSize)) : column;
    }

    /// <summary>The worse of two readiness values: Faulted, then Inactive, then Streaming, then Ready.</summary>
    /// <param name="a">One value.</param>
    /// <param name="b">The other.</param>
    /// <returns>The worse.</returns>
    public static PlanetTerrainReadiness Worse(PlanetTerrainReadiness a, PlanetTerrainReadiness b) => Rank(a) >= Rank(b) ? a : b;

    private static int Rank(PlanetTerrainReadiness value) => value switch
    {
        PlanetTerrainReadiness.Faulted => 3,
        PlanetTerrainReadiness.Inactive => 2,
        PlanetTerrainReadiness.Streaming => 1,
        _ => 0,
    };
}
