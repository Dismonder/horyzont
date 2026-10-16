using System.Numerics;
using System.Runtime.InteropServices;

using Horyzont.Core.Ids;
using Horyzont.Core.Props;
using Horyzont.Core.World;

namespace Horyzont.Server.Props;

/// <summary>
/// The regions of props around a point of a planet (K6-31 D17): those whose centre is within a distance. The candidates come from a grid
/// of sample points on the tangent plane (each maps to the region it falls into, the grid is finer than the smallest region), the answer is
/// the candidates whose centre is really within the distance. Keeps its scratch list, so a steady state allocates nothing. Main thread only.
/// </summary>
public sealed class PropInterest
{
    private const int CellsPerRegionEdge = 8;

    // Sample spacing as a share of the width of a region at the middle of a face. Regions shrink towards the corners of a face to a little
    // over 0.6 of that width (equal-angle grid), so the circle inscribed in the smallest region has a radius of at least 0.25 of it, and a
    // grid of this spacing (covering radius 0.21) puts a sample in every region.
    private const float SampleSpacing = 0.3f;

    // Above this angle (radians) the reach covers so much of the planet that the tangent plane is no longer a fair picture of it.
    private const float MaxPlaneAngle = 1.0f;

    private readonly List<ulong> _values = [];

    /// <summary>The distance from a point to the centre of a region, taken at the radius of the planet.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="feetInPlanet">The point in the frame of the planet.</param>
    /// <param name="region">The region.</param>
    /// <returns>The distance in metres; infinity for a planet without a grid.</returns>
    public static float DistanceToRegion(PlanetParams planet, Vector3 feetInPlanet, PropRegionKey region)
    {
        ArgumentNullException.ThrowIfNull(planet);
        if (!CubeSphereGrid.TryGetCellsPerFace(planet.RadiusMeters, out var cells) || !region.IsValid)
        {
            return float.PositiveInfinity;
        }

        var centre = CubeSphereGrid.DirectionOfPoint(
            region.Face, (region.RegionU * CellsPerRegionEdge) + (CellsPerRegionEdge / 2.0), (region.RegionV * CellsPerRegionEdge) + (CellsPerRegionEdge / 2.0),
            cells);
        return Vector3.Distance(feetInPlanet, centre * planet.RadiusMeters);
    }

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
        var length = feetInPlanet.Length();
        if (!CubeSphereGrid.TryGetCellsPerFace(planet.RadiusMeters, out var cells) || !float.IsFinite(length) || !float.IsFinite(meters) || meters < 0f)
        {
            return;
        }

        var radius = planet.RadiusMeters;
        var regionsPerFace = cells / CellsPerRegionEdge;
        var regionMeters = radius * MathF.PI / 2f / regionsPerFace;

        // The reach plus the diagonal of a region: a sample inside a region that is in range may lie that much farther than its centre.
        var angle = (meters + (2f * regionMeters)) / radius;
        if (length < 1f || angle > MaxPlaneAngle)
        {
            AddAllRegions(regionsPerFace);
        }
        else
        {
            AddSampledRegions(feetInPlanet / length, radius, cells, regionMeters, MathF.Tan(angle) * radius);
        }

        CollectionsMarshal.AsSpan(_values).Sort();
        ulong previous = ulong.MaxValue;
        foreach (var value in _values)
        {
            if (value == previous)
            {
                continue;
            }

            previous = value;
            var region = new PropRegionKey(value);
            if (DistanceToRegion(planet, feetInPlanet, region) <= meters)
            {
                regions.Add(region);
            }
        }
    }

    private static ulong KeyOf(int face, int regionU, int regionV) => ((ulong)(uint)face << 34) | ((ulong)(uint)regionU << 17) | (uint)regionV;

    private void AddAllRegions(int regionsPerFace)
    {
        for (var face = 0; face < 6; face++)
        {
            for (var u = 0; u < regionsPerFace; u++)
            {
                for (var v = 0; v < regionsPerFace; v++)
                {
                    _values.Add(KeyOf(face, u, v));
                }
            }
        }
    }

    private void AddSampledRegions(Vector3 up, float radius, int cells, float regionMeters, float reachOnPlane)
    {
        var axis = MathF.Abs(up.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
        var east = Vector3.Normalize(Vector3.Cross(up, axis));
        var north = Vector3.Cross(up, east);
        var step = SampleSpacing * regionMeters;
        var n = (int)MathF.Ceiling(reachOnPlane / step);
        var limit = (reachOnPlane + step) * (reachOnPlane + step);
        for (var i = -n; i <= n; i++)
        {
            for (var j = -n; j <= n; j++)
            {
                var x = i * step;
                var y = j * step;
                if ((x * x) + (y * y) > limit)
                {
                    continue;
                }

                var direction = Vector3.Normalize((up * radius) + (east * x) + (north * y));
                CubeSphereGrid.CellOfDirection(direction, cells, out var face, out var cellU, out var cellV);
                _values.Add(KeyOf(face, cellU / CellsPerRegionEdge, cellV / CellsPerRegionEdge));
            }
        }
    }
}
