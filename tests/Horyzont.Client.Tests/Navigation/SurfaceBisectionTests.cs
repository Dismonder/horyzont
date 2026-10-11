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
    public void SurfaceBisection_NoSignChange_ReturnsNominalRadius()
    {
        var allAir = SurfaceBisection.FindSurfaceRadius(_ => 1f, Vector3.UnitX, 1000f, out _);
        Assert.Equal(1000f, allAir);
        var allSolid = SurfaceBisection.FindSurfaceRadius(_ => -1f, Vector3.UnitX, 1000f, out _);
        Assert.Equal(1000f, allSolid);
    }

    [Theory]
    [InlineData(-299.8f)]
    [InlineData(299.8f)]
    public void SurfaceBisection_SurfaceAtEdgeOfRange_IsFoundNotNominal(float offset)
    {
        var found = SurfaceBisection.FindSurfaceRadius(p => p.Length() - (1000f + offset), Vector3.UnitX, 1000f, out _);
        Assert.InRange(found, 1000f + offset - SurfaceBisection.DefaultTolerance, 1000f + offset + SurfaceBisection.DefaultTolerance);
    }

    [Fact]
    public void SurfaceBisection_NullField_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SurfaceBisection.FindSurfaceRadius(null!, Vector3.UnitX, 1000f, out _));
    }
}
