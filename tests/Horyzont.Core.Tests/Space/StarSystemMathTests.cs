using FsCheck.Fluent;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Space;
using Horyzont.Core.Tests.Serialization;
using Horyzont.Core.World;

namespace Horyzont.Core.Tests.Space;

public sealed class StarSystemMathTests
{
    private const double Millimetre = 0.001;

    [Fact]
    public void StarSystemMath_Gravity_NoJumpPerMillimetre() =>
        Kit.ForAll(
            Gen.Choose(0, 3).SelectMany(body => Gen.Choose(0, 1_000_000).Select(fraction => (body, fraction))),
            pair =>
            {
                var (radius, g0) = TestBodies[pair.body];
                var r = 3.3 * radius * pair.fraction / 1_000_000d;
                var step = Math.Abs(StarSystemMath.Gravity(r + Millimetre, radius, g0) - StarSystemMath.Gravity(r, radius, g0));
                Assert.True(step <= 1e-5, $"body {pair.body}, r {r}: step {step}");
            });

    [Fact]
    public void StarSystemMath_Gravity_NoJumpAtZoneBoundaries()
    {
        foreach (var (radius, g0) in TestBodies)
        {
            foreach (var factor in new[] { 1.0, StarSystemRules.GravityFadeStartFactor, StarSystemRules.ZoneRadiusFactor })
            {
                var r = factor * radius;
                var below = StarSystemMath.Gravity(r - Millimetre, radius, g0);
                var above = StarSystemMath.Gravity(r + Millimetre, radius, g0);
                Assert.InRange(Math.Abs(above - below), 0, 2e-5);
            }
        }
    }

    [Fact]
    public void StarSystemMath_Gravity_ExactAtBoundaries()
    {
        foreach (var (radius, g0) in TestBodies)
        {
            Assert.Equal(g0, StarSystemMath.Gravity(radius, radius, g0));
            Assert.Equal(0.16 * g0, StarSystemMath.Gravity(2.5 * radius, radius, g0), 1e-12);
            Assert.Equal(0d, StarSystemMath.Gravity(3 * radius, radius, g0));
            Assert.Equal(0d, StarSystemMath.Gravity((3 * radius) + 1e-6, radius, g0));
            Assert.Equal(0d, StarSystemMath.Gravity(10 * radius, radius, g0));
        }
    }

    [Fact]
    public void StarSystemMath_Gravity_ZeroAtCentreAndOutside()
    {
        var planet = new PlanetParams(new PlanetId(5), new SystemId(1), 1, "core:planet/1");
        Assert.Equal(Vector3D.Zero, StarSystemMath.GravityInPlanet(Vector3D.Zero, planet));
        Assert.Equal(Vector3D.Zero, StarSystemMath.GravityInPlanet(new Vector3D(0, 12_001, 0), planet));

        var inside = StarSystemMath.GravityInPlanet(new Vector3D(0, 2000, 0), planet);
        Assert.Equal(new Vector3D(0, -9.81f, 0), inside);

        var high = StarSystemMath.GravityInPlanet(new Vector3D(8000, 0, 0), planet);
        Assert.Equal(-9.81f * 0.25, high.X, 1e-9);
        Assert.Equal(0d, high.Y);
        Assert.Equal(0d, high.Z);
    }

    [Fact]
    public void StarSystemMath_SunDirection_MatchesDayNightCycleBitwise()
    {
        foreach (var rate in new[] { 20, 30, 60 })
        {
            foreach (var cycle in new[] { 1800f, 1200f, 7.3f })
            {
                for (ulong i = 0; i < 1000; i++)
                {
                    var tick = (i * 7919) + (i % 3 == 0 ? (ulong)uint.MaxValue * i : 0);
                    var expected = OldSunDirection(tick, rate, cycle);
                    var actual = StarSystemMath.SunDirection(tick, rate, cycle);
                    Assert.Equal(BitConverter.SingleToInt32Bits(expected.X), BitConverter.SingleToInt32Bits((float)actual.X));
                    Assert.Equal(BitConverter.SingleToInt32Bits(expected.Y), BitConverter.SingleToInt32Bits((float)actual.Y));
                    Assert.Equal(BitConverter.SingleToInt32Bits(expected.Z), BitConverter.SingleToInt32Bits((float)actual.Z));
                }
            }
        }
    }

    [Fact]
    public void StarSystemMath_SunDirection_PeriodIs1800s()
    {
        const int rate = 60;
        var period = 1800UL * rate;
        Assert.Equal(new Vector3D(1, 0, 0), StarSystemMath.SunDirection(0, rate, 1800f));
        Assert.Equal(StarSystemMath.SunDirection(12_345, rate, 1800f), StarSystemMath.SunDirection(12_345 + period, rate, 1800f));
        var quarter = StarSystemMath.SunDirection(period / 4, rate, 1800f);
        Assert.Equal(0, quarter.X, 1e-12);
        Assert.Equal(-1, quarter.Z, 1e-12);
        Assert.NotEqual(StarSystemMath.SunDirection(1, rate, 1800f), StarSystemMath.SunDirection(1 + (period / 2), rate, 1800f));
        Assert.Throws<ArgumentOutOfRangeException>(() => StarSystemMath.SunDirection(0, 0, 1800f));
        Assert.Throws<ArgumentOutOfRangeException>(() => StarSystemMath.SunDirection(0, rate, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => StarSystemMath.SunDirection(0, rate, 0f));
    }

    private static (double Radius, double G0)[] TestBodies { get; } = [(4000, 9.81f), (1400, 1.62f), (3500, 9.0f), (4500, 12.0f)];

    // Oracle: the formula of DayNightCycle.SunDirection before K5-21 (game/src/Planet/Sky/DayNightCycle.cs).
    private static System.Numerics.Vector3 OldSunDirection(ulong tick, int tickRate, float dayLengthSeconds)
    {
        var dayTicks = Math.Round((double)dayLengthSeconds * tickRate);
        var period = (ulong)dayTicks;
        var phase = tick % period;
        var angle = 2d * Math.PI * ((double)phase / period);
        return new System.Numerics.Vector3((float)Math.Cos(angle), 0f, (float)-Math.Sin(angle));
    }
}
