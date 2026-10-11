using System.Numerics;

using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Simulation;

/// <summary>Where players appear: the world start until K6-35 adds beds (K5-25 decision 8).</summary>
public static class RespawnPoints
{
    /// <summary>
    /// The world start on a planet: the terrain under the top of the atmosphere above the pole, lifted 0.1 m; the surface
    /// radius when the ray finds no terrain.
    /// </summary>
    /// <param name="planet">The planet (the home planet for the world start).</param>
    /// <param name="terrain">Terrain service.</param>
    /// <returns>Feet in the planet frame.</returns>
    public static Vector3 WorldStart(PlanetParams planet, ITerrainService terrain)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(terrain);
        var top = planet.RadiusMeters + planet.AtmosphereHeightMeters;
        var origin = new Vector3(0f, top, 0f);
        if (terrain.Raycast(planet.Id, origin, -Vector3.UnitY, top, out var hit))
        {
            return hit.Position + (TerrainMath.UpAt(hit.Position) * 0.1f);
        }

        return new Vector3(0f, planet.RadiusMeters + 0.1f, 0f);
    }
}
