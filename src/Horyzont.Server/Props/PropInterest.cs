using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World;

namespace Horyzont.Server.Props;

/// <summary>
/// The regions of props around a point of a planet (K6-31 D17): those whose centre is within a distance. Keeps its scratch lists, so a
/// steady state allocates nothing. Main thread only.
/// </summary>
public sealed class PropInterest
{
    private readonly List<ulong> _values = [];

    /// <summary>Finds the regions whose centre lies within a distance of a point.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="feetInPlanet">The point in the frame of the planet.</param>
    /// <param name="meters">Distance in metres.</param>
    /// <param name="regions">Receives the regions ascending by value, without duplicates; cleared first.</param>
    public void CollectRegions(PlanetParams planet, Vector3 feetInPlanet, float meters, List<PropRegionKey> regions)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(regions);
        regions.Clear();
        _values.Clear();

        // K6-31 C1: skeleton.
    }

    /// <summary>The distance from a point to the centre of a region, taken at the radius of the planet.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="feetInPlanet">The point in the frame of the planet.</param>
    /// <param name="region">The region.</param>
    /// <returns>The distance in metres.</returns>
    public static float DistanceToRegion(PlanetParams planet, Vector3 feetInPlanet, PropRegionKey region)
    {
        ArgumentNullException.ThrowIfNull(planet);
        return float.NaN;
    }
}
