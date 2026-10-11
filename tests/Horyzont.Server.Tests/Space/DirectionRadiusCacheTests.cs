using System.Numerics;

using Horyzont.Server.Space;

namespace Horyzont.Server.Tests.Space;

public sealed class DirectionRadiusCacheTests
{
    private static readonly Func<Vector3, float> _radius = static d => 4000f + d.X;

    [Fact]
    public void DirectionRadiusCache_TwoShipsAlternating_ComputeOnceEach()
    {
        var cache = new DirectionRadiusCache();
        var a = Vector3.Normalize(new Vector3(1f, 1f, 0f));
        var b = Vector3.Normalize(new Vector3(-1f, 1f, 0.5f));

        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(_radius(a), cache.Get(a * 4100f, _radius), 3);
            Assert.Equal(_radius(b), cache.Get(b * 3900f, _radius), 3);
        }

        Assert.Equal(2, cache.Misses);
    }

    [Fact]
    public void DirectionRadiusCache_Full_EvictsOldestFirst()
    {
        var cache = new DirectionRadiusCache();
        for (var i = 0; i <= DirectionRadiusCache.Capacity; i++)
        {
            cache.Get(new Vector3(MathF.Cos(i), 1f, MathF.Sin(i)), _radius);
        }

        Assert.Equal(DirectionRadiusCache.Capacity + 1, cache.Misses);
        cache.Get(new Vector3(MathF.Cos(DirectionRadiusCache.Capacity), 1f, MathF.Sin(DirectionRadiusCache.Capacity)), _radius);
        Assert.Equal(DirectionRadiusCache.Capacity + 1, cache.Misses);
        cache.Get(new Vector3(MathF.Cos(0), 1f, MathF.Sin(0)), _radius);
        Assert.Equal(DirectionRadiusCache.Capacity + 2, cache.Misses);
    }
}
