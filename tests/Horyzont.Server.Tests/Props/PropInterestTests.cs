using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Props;
using Horyzont.Core.World;
using Horyzont.Server.Props;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Props;

/// <summary>The regions of props near a point (K6-31 D17): those whose centre is within a distance, found without walking every region.</summary>
public sealed class PropInterestTests
{
    private static PlanetParams Planet => WorldFactory.HomePlanetParams;

    private static PropRegionKey Region(int face, int u, int v) => new(((ulong)(uint)face << 34) | ((ulong)(uint)u << 17) | (uint)v);

    /// <summary>Every region of the planet whose centre is within the distance, by measuring all of them (57 624 on a planet of 4000 m).</summary>
    private static List<PropRegionKey> BruteForce(Vector3 feet, float meters)
    {
        Assert.True(CubeSphereGrid.TryGetCellsPerFace(Planet.RadiusMeters, out var cells));
        var regionsPerFace = cells / 8;
        var all = new List<PropRegionKey>();
        for (var face = 0; face < 6; face++)
        {
            for (var u = 0; u < regionsPerFace; u++)
            {
                for (var v = 0; v < regionsPerFace; v++)
                {
                    var region = Region(face, u, v);
                    if (PropInterest.DistanceToRegion(Planet, feet, region) <= meters)
                    {
                        all.Add(region);
                    }
                }
            }
        }

        return [.. all.OrderBy(r => r.Value)];
    }

    [Theory]
    [InlineData(0f, 4005f, 0f)]
    [InlineData(2000f, 3000f, 1500f)]
    [InlineData(2300f, 2300f, 2300f)]
    [InlineData(-3900f, 800f, 800f)]
    public void Regions_AreExactlyThoseWithTheCentreInRange(float x, float y, float z)
    {
        var feet = Vector3.Normalize(new Vector3(x, y, z)) * 4005f;
        var interest = new PropInterest();
        var regions = new List<PropRegionKey>();

        interest.CollectRegions(Planet, feet, 512f, regions);

        var expected = BruteForce(feet, 512f);
        // Near the corner of a face the regions are smaller, so more of them fit in 512 m (the replicator sends the surplus with the next scan).
        Assert.InRange(expected.Count, 100, 300);
        Assert.Equal(expected, regions);
    }

    [Fact]
    public void Regions_ContainTheRegionOfTheFeet_SortedAndUnique()
    {
        var feet = new Vector3(0f, 4005f, 0f);
        var interest = new PropInterest();
        var regions = new List<PropRegionKey>();

        interest.CollectRegions(Planet, feet, 512f, regions);

        Assert.True(CubeSphereGrid.TryGetCellsPerFace(Planet.RadiusMeters, out var cells));
        CubeSphereGrid.CellOfDirection(Vector3.Normalize(feet), cells, out var face, out var u, out var v);
        Assert.Contains(Region(face, u / 8, v / 8), regions);
        Assert.Equal(regions.Select(r => r.Value).Distinct().Order().ToArray(), regions.Select(r => r.Value).ToArray());
    }

    [Fact]
    public void Regions_WiderRange_IsASuperset()
    {
        var feet = new Vector3(0f, 4005f, 0f);
        var interest = new PropInterest();
        var enter = new List<PropRegionKey>();
        var leave = new List<PropRegionKey>();

        interest.CollectRegions(Planet, feet, 512f, enter);
        interest.CollectRegions(Planet, feet, 576f, leave);

        Assert.True(leave.Count > enter.Count);
        Assert.Subset(leave.ToHashSet(), enter.ToHashSet());
    }

    [Fact]
    public void Regions_ListIsClearedFirst_NoGrowthInSteadyState()
    {
        var feet = new Vector3(0f, 4005f, 0f);
        var planet = Planet;
        var interest = new PropInterest();
        var regions = new List<PropRegionKey>();
        interest.CollectRegions(planet, feet, 512f, regions);
        var first = regions.ToArray();
        interest.CollectRegions(planet, feet, 512f, regions);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20; i++)
        {
            interest.CollectRegions(planet, feet, 512f, regions);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(first, regions);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Distance_ToTheRegionOfTheFeet_IsSmall()
    {
        var feet = new Vector3(0f, 4005f, 0f);
        Assert.True(CubeSphereGrid.TryGetCellsPerFace(Planet.RadiusMeters, out var cells));
        CubeSphereGrid.CellOfDirection(Vector3.Normalize(feet), cells, out var face, out var u, out var v);

        var distance = PropInterest.DistanceToRegion(Planet, feet, Region(face, u / 8, v / 8));

        Assert.InRange(distance, 0f, 60f);
    }
}
