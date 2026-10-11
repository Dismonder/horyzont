using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Server.Space;

/// <summary>
/// Terrain readiness for flight (K5-22 decision 6, README Y11). Implemented by the game's <c>ServerPlanetTerrains</c>; K5-24 consumes
/// it through its terrain gate (adapter <c>PlanetTerrainShipGate</c>) with <see cref="PlanetTerrainReadinessQueries.GetShipReadiness"/>
/// on a <see cref="ShipTerrainQuery"/> (the sphere of <see cref="TerrainColumn"/> and, near the terrain, the sweep box of
/// <see cref="TerrainSweep"/>; Opus review of K5-22).
/// Main thread only (it reads the terrain nodes); allocation-free.
/// </summary>
public interface IPlanetTerrainReadiness
{
    /// <summary>Gets the readiness of the terrain of a planet in a sphere.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="localCenter">Centre of the sphere in the planet frame (metres, origin at the planet centre).</param>
    /// <param name="radius">Radius of the sphere in metres (positive).</param>
    /// <returns>
    /// <see cref="PlanetTerrainReadiness.Ready"/> only when the LOD 0 collision is built (never on loaded data alone) — in the whole box
    /// of a sphere up to <see cref="TerrainSweep.FullBoxMaxRadius"/>, in the ground box of a larger one (see
    /// <see cref="PlanetTerrainReadiness.Ready"/>);
    /// <see cref="PlanetTerrainReadiness.Inactive"/> for a planet without terrain, including an unknown planet.
    /// </returns>
    PlanetTerrainReadiness GetReadiness(PlanetId planet, Vector3 localCenter, float radius);
}
