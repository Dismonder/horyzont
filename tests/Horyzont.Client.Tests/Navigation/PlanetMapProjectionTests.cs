using System.Numerics;

using Horyzont.Client.Navigation;

namespace Horyzont.Client.Tests.Navigation;

public sealed class PlanetMapProjectionTests
{
    [Theory]
    [InlineData(0.1f, 0.2f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0.9f, 0.8f)]
    [InlineData(0.25f, 0.6f)]
    public void PlanetMapProjection_DirectionRoundTrip_ReturnsSamePoint(float u, float v)
    {
        var direction = PlanetMapProjection.Direction(u, v);
        Assert.Equal(1f, direction.Length(), 4);
        Assert.True(PlanetMapProjection.TryFromPosition(direction * 1234f, out var u2, out var v2));
        Assert.Equal(u, u2, 3);
        Assert.Equal(v, v2, 3);
    }

    [Fact]
    public void PlanetMapProjection_Poles_AreTopAndBottom()
    {
        Assert.Equal(1f, PlanetMapProjection.Direction(0.3f, 0f).Y, 4);
        Assert.Equal(-1f, PlanetMapProjection.Direction(0.3f, 1f).Y, 4);
        Assert.True(PlanetMapProjection.TryFromPosition(new Vector3(0, 5, 0), out _, out var v));
        Assert.Equal(0f, v, 4);
    }

    [Fact]
    public void PlanetMapProjection_LongitudeZero_AlongPlusXAtHalfWidth()
    {
        var d = PlanetMapProjection.Direction(0.5f, 0.5f);
        Assert.Equal(1f, d.X, 4);
        Assert.True(PlanetMapProjection.TryFromPosition(new Vector3(7, 0, 0), out var u, out var v));
        Assert.Equal(0.5f, u, 4);
        Assert.Equal(0.5f, v, 4);
    }

    [Fact]
    public void PlanetMapProjection_CentreOrNonFinite_ReturnsFalse()
    {
        Assert.False(PlanetMapProjection.TryFromPosition(Vector3.Zero, out _, out _));
        Assert.False(PlanetMapProjection.TryFromPosition(new Vector3(float.NaN, 0, 0), out _, out _));
    }
}
