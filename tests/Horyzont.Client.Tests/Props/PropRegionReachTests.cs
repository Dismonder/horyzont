using System.Numerics;

using Horyzont.Client.Props;
using Horyzont.Core.Ids;
using Horyzont.Core.Props;
using Horyzont.Testing;

namespace Horyzont.Client.Tests.Props;

/// <summary>
/// The region filter in front of the picker (K6-31 review fix): a prop lies up to 300 m above or below the nominal radius, so a region whose
/// centre is on the nominal radius must not be skipped for an eye next to a prop that stands high above it.
/// </summary>
public sealed class PropRegionReachTests
{
    private const int CellsPerRegionEdge = 8;
    private static readonly PlanetId _planet = WorldFactory.HomePlanet;

    private static Vector3 CentreOf(PropRegionKey region)
    {
        var radius = WorldFactory.HomePlanetParams.RadiusMeters;
        Assert.True(CubeSphereGrid.TryGetCellsPerFace(radius, out var cells));
        return CubeSphereGrid.DirectionOfPoint(
            region.Face, (region.RegionU * CellsPerRegionEdge) + (CellsPerRegionEdge / 2.0), (region.RegionV * CellsPerRegionEdge) + (CellsPerRegionEdge / 2.0), cells)
            * radius;
    }

    /// <summary>Props of the layout standing at a height over the radius of the planet (the generated surface is moved there).</summary>
    private static List<PropInstance> PropsAt(float height)
    {
        var radius = WorldFactory.HomePlanetParams.RadiusMeters + height;
        var terrain = new FakeTerrainService(radius) { GeneratedSurfaceRadius = radius };
        var field = new PropField(WorldFactory.CreateWithProps(1), terrain);
        var found = new List<PropInstance>();
        field.CollectInSphere(_planet, new Vector3(0, radius, 0), 100f, found);
        Assert.NotEmpty(found);
        return found;
    }

    [Theory]
    [InlineData(-250f)]
    [InlineData(0f)]
    [InlineData(150f)]
    [InlineData(200f)]
    [InlineData(299f)]
    public void MayHold_EyeBesideAHighProp_KeepsTheRegionOfTheProp(float height)
    {
        var accepted = 0;
        foreach (var prop in PropsAt(height))
        {
            // Rocks, boulders and crystals are the kinds whose band reaches 300 m; the others refuse the height and are not found.
            var eye = prop.Anchor + (prop.Up * 1.7f);

            Assert.True(PropRegionReach.MayHold(CentreOf(prop.Id.Region), eye, 40f), $"{prop.Kind} at {height} m over the radius was skipped.");
            accepted++;
        }

        Assert.True(accepted > 0);
    }

    [Fact]
    public void MayHold_RegionFarFromTheSphere_IsSkipped()
    {
        var prop = PropsAt(0f)[0];
        var far = prop.Anchor + (Vector3.Normalize(Vector3.Cross(prop.Up, Vector3.UnitX)) * 2000f);

        Assert.False(PropRegionReach.MayHold(CentreOf(prop.Id.Region), far, 40f));
    }
}
