using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Space;
using Horyzont.Core.World;

namespace Horyzont.Core.Tests.Space;

public sealed class StarSystemModelTests
{
    private const string Profile = "core:planet/1";

    [Fact]
    public void StarSystemModel_HotPaths_AllocateZeroBytes()
    {
        var created = StarSystemGenesis.CreateWorldBodies(99, Profile);
        var model = new StarSystemModel(created.System, created.Bodies);
        var moon = created.Bodies[1];
        var points = new Vector3D[16];
        for (var i = 0; i < points.Length; i++)
        {
            var body = created.System.Bodies[i % 4].Pose.Position;
            points[i] = body + new Vector3D(i * 900, -i * 300, i * 50);
        }

        var sink = 0.0;
        for (var i = 0; i < 100; i++)
        {
            sink += Step(model, moon, points, i);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            sink += Step(model, moon, points, i);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(double.IsFinite(sink));

        static double Step(StarSystemModel model, PlanetParams moon, Vector3D[] points, int i)
        {
            var p = points[i % points.Length];
            var found = model.TryFindZone(p, out var index, out var distance) ? index + distance : 0;
            var g = model.GravityAt(p);
            var local = StarSystemMath.GravityInPlanet(p, moon);
            var sun = StarSystemMath.SunDirection((ulong)i, 60, 1800f);
            return found + g.X + local.Y + sun.Z;
        }
    }

    [Fact]
    public void StarSystemModel_TryFindZone_AtMostOneBody()
    {
        var random = new Random(1234);
        for (ulong seed = 0; seed < 50; seed++)
        {
            var created = StarSystemGenesis.CreateWorldBodies(seed, Profile);
            var model = new StarSystemModel(created.System, created.Bodies);
            for (var n = 0; n < 2_000; n++)
            {
                var body = created.System.Bodies[random.Next(4)].Pose.Position;
                var reach = created.Bodies[random.Next(4)].RadiusMeters * 3.3;
                var p = body + new Vector3D((random.NextDouble() * 2 - 1) * reach, (random.NextDouble() * 2 - 1) * reach, (random.NextDouble() * 2 - 1) * reach);

                var inside = 0;
                var expected = -1;
                for (var k = 0; k < model.BodyCount; k++)
                {
                    var info = model.GetBody(k);
                    if (Vector3D.Distance(p, info.Position) <= StarSystemRules.ZoneRadiusFactor * info.RadiusMeters)
                    {
                        inside++;
                        expected = k;
                    }
                }

                Assert.InRange(inside, 0, 1);
                Assert.Equal(inside == 1, model.TryFindZone(p, out var index, out var distance));
                if (inside == 1)
                {
                    Assert.Equal(expected, index);
                    Assert.Equal(Vector3D.Distance(p, model.GetBody(index).Position), distance, 1e-9);
                }
            }
        }
    }

    [Fact]
    public void StarSystemModel_GravityAt_PointsToBodyInsideZoneAndZeroBetween()
    {
        var created = StarSystemGenesis.CreateWorldBodies(5, Profile);
        var model = new StarSystemModel(created.System, created.Bodies);
        var moon = model.GetBody(1);

        var above = moon.Position + new Vector3D(0, 2 * moon.RadiusMeters, 0);
        var g = model.GravityAt(above);
        Assert.Equal(0, g.X, 1e-12);
        Assert.Equal(-moon.SurfaceGravity / 4, g.Y, 1e-9);

        var between = Vector3D.Normalize(moon.Position) * 12_600;
        Assert.Equal(Vector3D.Zero, model.GravityAt(between));
        Assert.True(model.TryGetBody(created.Bodies[2].Id, out var info));
        Assert.Equal(created.System.Bodies[2].Pose.Position, info.Position);
        Assert.False(model.TryGetBody(new PlanetId(12345), out _));
    }
}
