using System.Numerics;

using Horyzont.Client.Navigation;

namespace Horyzont.Client.Tests.Navigation;

public sealed class NavigationScreenMathTests
{
    private static readonly Vector2 _view = new(1280, 720);
    private const float Fov = 1.2f;

    [Fact]
    public void NavigationScreenMath_TargetInView_Pin()
    {
        Assert.True(NavigationScreenMath.TryPlace(new Vector3(0, 0, -1), _view, Fov, out var centre, out _));
        Assert.Equal(new Vector2(640, 360), centre);

        Assert.True(NavigationScreenMath.TryPlace(new Vector3(0.2f, 0.1f, -1), _view, Fov, out var pin, out _));
        Assert.True(pin.X > 640 && pin.Y < 360);
    }

    [Fact]
    public void NavigationScreenMath_BehindOrOutside_EdgeArrowInsideMargin()
    {
        // Behind the camera, to the right and slightly up.
        Assert.False(NavigationScreenMath.TryPlace(new Vector3(1, 0.2f, 1), _view, Fov, out var behind, out var angle));
        Assert.Equal(1280 - 32f, behind.X, 3);
        Assert.InRange(behind.Y, 32f, 720 - 32f);
        Assert.InRange(angle, -0.5f, 0.1f);

        // In front but far to the left, outside the view.
        Assert.False(NavigationScreenMath.TryPlace(new Vector3(-5, 0, -1), _view, Fov, out var left, out var leftAngle));
        Assert.Equal(32f, left.X, 3);
        Assert.Equal(MathF.PI, MathF.Abs(leftAngle), 3);

        // Straight behind and straight up: bottom or top edge.
        Assert.False(NavigationScreenMath.TryPlace(new Vector3(0, 0, 1), _view, Fov, out var back, out _));
        Assert.Equal(720 - 32f, back.Y, 3);
        Assert.False(NavigationScreenMath.TryPlace(new Vector3(0, 1, 0), _view, Fov, out var up, out _));
        Assert.Equal(32f, up.Y, 3);

        // Just inside the margin is still a pin; just outside becomes an arrow.
        Assert.False(NavigationScreenMath.TryPlace(new Vector3(0, 0.999f, -0.05f), _view, Fov, out _, out _));
    }

    [Fact]
    public void SurfaceBisection_FindsRadiusWithinToleranceInElevenSamples()
    {
        foreach (var surface in new[] { 3712.4f, 4000f, 4100.1f, 4299.9f })
        {
            var radius = SurfaceBisection.FindSurfaceRadius(p => p.Length() - surface, Vector3.UnitX, 4000f, out var samples);
            Assert.InRange(MathF.Abs(radius - surface), 0f, 0.5f);
            Assert.True(samples <= 12, $"{samples} samples"); // 11 plus one check of the range end when the search ends there
        }

        // Without a sign change the nominal radius is returned.
        var flat = SurfaceBisection.FindSurfaceRadius(_ => 1f, Vector3.UnitY, 4000f, out var used);
        Assert.Equal(4000f, flat);
        Assert.True(used <= 12);
    }

    [Fact]
    public void PlanetMapProjection_DirectionAndPosition_RoundTrip()
    {
        foreach (var (u, v) in new[] { (0.1f, 0.2f), (0.5f, 0.5f), (0.9f, 0.8f), (0.25f, 0.05f) })
        {
            var direction = PlanetMapProjection.Direction(u, v);
            Assert.Equal(1f, direction.Length(), 4);
            Assert.True(PlanetMapProjection.TryFromPosition(direction * 4123f, out var u2, out var v2));
            Assert.Equal(u, u2, 4);
            Assert.Equal(v, v2, 4);
        }

        // The north pole is at the top, longitude 0 along +X is the middle of the map.
        Assert.True(PlanetMapProjection.TryFromPosition(new Vector3(0, 100, 0), out _, out var top));
        Assert.Equal(0f, top, 4);
        Assert.True(PlanetMapProjection.TryFromPosition(new Vector3(100, 0, 0), out var middle, out var equator));
        Assert.Equal(0.5f, middle, 4);
        Assert.Equal(0.5f, equator, 4);
        Assert.False(PlanetMapProjection.TryFromPosition(Vector3.Zero, out _, out _));
    }
}
