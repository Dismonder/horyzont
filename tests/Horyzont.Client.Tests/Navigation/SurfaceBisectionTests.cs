using System.Numerics;

using Horyzont.Client.Navigation;

namespace Horyzont.Client.Tests.Navigation;

public sealed class SurfaceBisectionTests
{
    [Theory]
    [InlineData(120f, 5000f)]
    [InlineData(-250f, 5000f)]
    [InlineData(299f, 12000f)]
    public void SurfaceBisection_SphereWithOffset_FindsRadiusWithinToleranceAndEleven_Samples(float offset, float radius)
    {
        var surface = radius + offset;
        var found = SurfaceBisection.FindSurfaceRadius(p => p.Length() - surface, Vector3.UnitY, radius, out var samples);
        Assert.InRange(found, surface - SurfaceBisection.DefaultTolerance, surface + SurfaceBisection.DefaultTolerance);
        Assert.True(samples <= 11);
    }

    [Fact]
    public void SurfaceBisection_NoSignChange_ReturnsEdgeOfRange()
    {
        var allAir = SurfaceBisection.FindSurfaceRadius(_ => 1f, Vector3.UnitX, 1000f, out _);
        Assert.InRange(allAir, 700f, 700f + SurfaceBisection.DefaultTolerance);
        var allSolid = SurfaceBisection.FindSurfaceRadius(_ => -1f, Vector3.UnitX, 1000f, out _);
        Assert.InRange(allSolid, 1300f - SurfaceBisection.DefaultTolerance, 1300f);
    }

    [Fact]
    public void SurfaceBisection_NullField_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SurfaceBisection.FindSurfaceRadius(null!, Vector3.UnitX, 1000f, out _));
    }
}
