using Horyzont.Core.World;

namespace Horyzont.Game.UI.Map;

/// <summary>
/// Planet maps of a session by (generator profile, seed, radius), so a map is sampled once (K5-27 decision 12). One instance per
/// session, owned by the map controller; main thread only. K5-22 replaces the sampling by <c>PlanetSurfaceSampler</c> without changing this seam.
/// </summary>
public sealed class PlanetMapCache
{
    private readonly Dictionary<(string Profile, ulong Seed, float Radius), PlanetMap> _maps = [];

    /// <summary>Gets the map of a planet, starting the sampling on first use.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>The map; it becomes ready after <see cref="PlanetMap.Poll"/> sees the workers finished.</returns>
    public PlanetMap Get(PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        var key = (planet.GeneratorProfile, planet.Seed, planet.RadiusMeters);
        if (!_maps.TryGetValue(key, out var map))
        {
            map = new PlanetMap(planet);
            _maps[key] = map;
            map.Start();
        }

        return map;
    }
}
