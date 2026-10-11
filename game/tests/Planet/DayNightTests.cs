using GdUnit4;

using Godot;

using Horyzont.Core.World;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Planet.Sky;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Planet;

[TestSuite]
public class DayNightTests
{
    private const int TickRate = 60;

    private static PlanetParams Planet(float dayLength = 1800f) =>
        PlanetGenesis.CreatePlanet(1, 0, GeneratorProfiles.Current) with { DayLengthSeconds = dayLength };

    private static void AssertVector(System.Numerics.Vector3 actual, float x, float y, float z, float tolerance = 1e-6f)
    {
        AssertThat(MathF.Abs(actual.X - x)).IsLess(tolerance);
        AssertThat(MathF.Abs(actual.Y - y)).IsLess(tolerance);
        AssertThat(MathF.Abs(actual.Z - z)).IsLess(tolerance);
    }

    [TestCase]
    public void DayNight_SunDirection_GoldenAndPeriodic()
    {
        var planet = Planet();
        var dayTicks = (ulong)(1800 * TickRate);

        // Tick 0: noon at the start point on the equator, the sun is on +X.
        AssertVector(DayNightCycle.SunDirection(0, TickRate, planet), 1f, 0f, 0f);

        // A quarter of a day: rotated by 90 degrees around +Y (+X goes to -Z).
        AssertVector(DayNightCycle.SunDirection(dayTicks / 4, TickRate, planet), 0f, 0f, -1f);
        AssertVector(DayNightCycle.SunDirection(dayTicks / 2, TickRate, planet), -1f, 0f, 0f);
        AssertVector(DayNightCycle.SunDirection(dayTicks * 3 / 4, TickRate, planet), 0f, 0f, 1f);

        // Periodic: tick and tick + whole days give the same vector.
        foreach (var tick in new ulong[] { 1, 777, 54321, dayTicks - 1 })
        {
            var a = DayNightCycle.SunDirection(tick, TickRate, planet);
            AssertVector(DayNightCycle.SunDirection(tick + dayTicks, TickRate, planet), a.X, a.Y, a.Z);
            AssertVector(DayNightCycle.SunDirection(tick + (dayTicks * 1000), TickRate, planet), a.X, a.Y, a.Z);
        }

        // A tick near ulong.MaxValue: still a unit vector, no precision loss from huge angles.
        foreach (var tick in new[] { ulong.MaxValue, ulong.MaxValue - 12345, ulong.MaxValue / 2 })
        {
            var v = DayNightCycle.SunDirection(tick, TickRate, planet);
            AssertThat(MathF.Abs(v.Length() - 1f)).IsLess(1e-6f);
            AssertThat(v.Y).IsEqual(0f);
        }

        // Bad arguments.
        AssertThrown(() => DayNightCycle.SunDirection(0, 0, planet)).IsInstanceOf<ArgumentOutOfRangeException>();
        AssertThrown(() => DayNightCycle.SunDirection(0, -5, planet)).IsInstanceOf<ArgumentOutOfRangeException>();
        AssertThrown(() => DayNightCycle.SunDirection(0, TickRate, Planet(0f))).IsInstanceOf<ArgumentOutOfRangeException>();
        AssertThrown(() => DayNightCycle.SunDirection(0, TickRate, Planet(-1f))).IsInstanceOf<ArgumentOutOfRangeException>();
        AssertThrown(() => DayNightCycle.SunDirection(0, TickRate, Planet(float.NaN))).IsInstanceOf<ArgumentOutOfRangeException>();
    }

    [TestCase]
    public void DayNight_SunDirection_BitwiseEqualsPreK5_21Formula()
    {
        // K5-21 moved the formula to StarSystemMath.SunDirection; the planet overload must stay bit for bit the same.
        foreach (var dayLength in new[] { 1800f, 1200f, 7.3f })
        {
            var planet = Planet(dayLength);
            foreach (var tick in new ulong[] { 0, 1, 7000, 27000, 54000, 95555, 1_000_003, (ulong)uint.MaxValue * 3, ulong.MaxValue })
            {
                var expected = OldSunDirection(tick, TickRate, dayLength);
                var actual = DayNightCycle.SunDirection(tick, TickRate, planet);
                AssertThat(BitConverter.SingleToUInt32Bits(actual.X)).IsEqual(BitConverter.SingleToUInt32Bits(expected.X));
                AssertThat(BitConverter.SingleToUInt32Bits(actual.Y)).IsEqual(BitConverter.SingleToUInt32Bits(expected.Y));
                AssertThat(BitConverter.SingleToUInt32Bits(actual.Z)).IsEqual(BitConverter.SingleToUInt32Bits(expected.Z));
            }
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task DayNight_SunLight_AppliesDirection()
    {
        var planet = Planet();
        await using var scene = EngineTestScene.Create();
        var light = scene.Add(new SunLight());

        foreach (var tick in new ulong[] { 0, 7000, 27000, 54000, 95555 })
        {
            light.Apply(tick, TickRate, planet);
            var sun = DayNightCycle.SunDirection(tick, TickRate, planet);
            var expected = new Vector3(-sun.X, -sun.Y, -sun.Z);
            var forward = -light.GlobalTransform.Basis.Z;

            AssertThat(Math.Abs(forward.X - expected.X)).IsLess(1e-5);
            AssertThat(Math.Abs(forward.Y - expected.Y)).IsLess(1e-5);
            AssertThat(Math.Abs(forward.Z - expected.Z)).IsLess(1e-5);
        }
    }

    // The inlined formula of DayNightCycle.SunDirection before K5-21.
    private static System.Numerics.Vector3 OldSunDirection(ulong tick, int tickRate, float dayLengthSeconds)
    {
        var period = (ulong)Math.Round((double)dayLengthSeconds * tickRate);
        var phase = tick % period;
        var angle = 2d * Math.PI * ((double)phase / period);
        return new System.Numerics.Vector3((float)Math.Cos(angle), 0f, (float)-Math.Sin(angle));
    }
}
