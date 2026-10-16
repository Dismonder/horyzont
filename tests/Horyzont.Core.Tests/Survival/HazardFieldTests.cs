using System.Numerics;

using Horyzont.Core.Content;
using Horyzont.Core.Survival;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-34, D9 and D10: the hazards at a point as a pure function of the profile, the height, the time of day and the intensity.</summary>
public sealed class HazardFieldTests
{
    private static readonly HazardRates _ones = new(1f, 1f, 1f, 1f, 1f);

    /// <summary>Day: heat 0.3, toxins 0.4, radiation 0.2. Night: cold 0.4, toxins 0.4. No zones.</summary>
    private static readonly HazardProfileDef _profile = new("test:p", new HazardRates(0f, 0.3f, 0.4f, 0.2f, 0f), new HazardRates(0.4f, 0f, 0.4f, 0f, 0f), []);

    private static HazardProfileDef WithZones(params (float MinHeight, float ColdScale)[] zones) =>
        _profile with { Zones = [.. zones.Select(z => new HazardZoneDef(z.MinHeight, _ones with { Cold = z.ColdScale }))] };

    [Fact]
    public void Rates_DaylightOutside01_Clamped()
    {
        Assert.Equal(_profile.Night, HazardField.Rates(_profile, 0f, -0.5f, 1f));
        Assert.Equal(_profile.Day, HazardField.Rates(_profile, 0f, 1.5f, 1f));
    }

    [Fact]
    public void Daylight_BandNotPositive_Throws()
    {
        Assert.Equal(1f, HazardField.Daylight(Vector3.UnitY, Vector3.UnitY, 0.1f));
        foreach (var band in new[] { 0f, -0.1f, float.NaN })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HazardField.Daylight(Vector3.UnitY, Vector3.UnitY, band));
        }
    }

    [Fact]
    public void Rates_DayNightBlend()
    {
        var night = HazardField.Rates(_profile, 0f, 0f, 1f);
        var day = HazardField.Rates(_profile, 0f, 1f, 1f);
        var quarter = HazardField.Rates(_profile, 0f, 0.25f, 1f);
        var half = HazardField.Rates(_profile, 0f, 0.5f, 1f);

        Assert.Equal(_profile.Night, night);
        Assert.Equal(_profile.Day, day);

        // night + (day - night) * daylight, for each of the five hazards.
        Assert.Equal(0.3f, quarter.Cold, 5);
        Assert.Equal(0.075f, quarter.Heat, 5);
        Assert.Equal(0.4f, quarter.Toxins, 5);
        Assert.Equal(0.05f, quarter.Radiation, 5);
        Assert.Equal(0f, quarter.Vacuum, 5);
        Assert.Equal(0.2f, half.Cold, 5);
        Assert.Equal(0.15f, half.Heat, 5);
        Assert.Equal(0.1f, half.Radiation, 5);
    }

    [Fact]
    public void Daylight_TwilightBand()
    {
        var up = new Vector3(0f, 1f, 0f);
        static Vector3 SunAt(float dot) => new(MathF.Sqrt(1f - (dot * dot)), dot, 0f);

        // Band 0.1: full day from dot 0.1 up, full night from -0.1 down, linear in between.
        Assert.Equal(1f, HazardField.Daylight(up, SunAt(1f), 0.1f), 5);
        Assert.Equal(1f, HazardField.Daylight(up, SunAt(0.1f), 0.1f), 5);
        Assert.Equal(0.75f, HazardField.Daylight(up, SunAt(0.05f), 0.1f), 5);
        Assert.Equal(0.5f, HazardField.Daylight(up, SunAt(0f), 0.1f), 5);
        Assert.Equal(0.25f, HazardField.Daylight(up, SunAt(-0.05f), 0.1f), 5);
        Assert.Equal(0f, HazardField.Daylight(up, SunAt(-0.1f), 0.1f), 5);
        Assert.Equal(0f, HazardField.Daylight(up, SunAt(-1f), 0.1f), 5);

        // A wider band blends over a wider range.
        Assert.Equal(0.75f, HazardField.Daylight(up, SunAt(0.25f), 0.5f), 5);
    }

    [Fact]
    public void Zones_LastMatchingApplies()
    {
        // Cold 0.4 at night; zones from 0 m (x1.5), 150 m (x3) and 1000 m (x0.5): the last zone whose minimum is at most the height applies, none below the first.
        var profile = WithZones((0f, 1.5f), (150f, 3f), (1000f, 0.5f));
        float ColdAt(float height) => HazardField.Rates(profile, height, 0f, 1f).Cold;

        Assert.Equal(0.4f, ColdAt(-10f), 5);
        Assert.Equal(0.6f, ColdAt(0f), 5);
        Assert.Equal(0.6f, ColdAt(149.9f), 5);
        Assert.Equal(1.2f, ColdAt(150f), 5);
        Assert.Equal(1.2f, ColdAt(999f), 5);
        Assert.Equal(0.2f, ColdAt(1000f), 5);
        Assert.Equal(0.2f, ColdAt(1_000_000f), 5);

        // The zone scales only the hazards it names (the others are 1): toxins stay 0.4.
        Assert.Equal(0.4f, HazardField.Rates(profile, 150f, 0f, 1f).Toxins, 5);

        // A profile without zones does not scale at all.
        Assert.Equal(0.4f, HazardField.Rates(_profile, 5000f, 0f, 1f).Cold, 5);
    }

    [Fact]
    public void Intensity_ScalesDrain()
    {
        static float ColdAt(float intensity) => HazardField.Rates(_profile, 0f, 0f, intensity).Cold;

        Assert.Equal(0.4f, ColdAt(1f), 5);
        Assert.Equal(0.2f, ColdAt(0.5f), 5);
        Assert.Equal(0.8f, ColdAt(2f), 5);

        // The world intensity is 0.5 to 2: a value outside is clamped.
        Assert.Equal(0.2f, ColdAt(0.1f), 5);
        Assert.Equal(0.2f, ColdAt(0f), 5);
        Assert.Equal(0.8f, ColdAt(5f), 5);
    }

    [Fact]
    public void NonFinite_Zero()
    {
        var zero = default(HazardRates);

        Assert.Equal(zero, HazardField.Rates(_profile, float.NaN, 0f, 1f));
        Assert.Equal(zero, HazardField.Rates(_profile, float.PositiveInfinity, 0f, 1f));
        Assert.Equal(zero, HazardField.Rates(_profile, float.NegativeInfinity, 0f, 1f));
        Assert.Equal(zero, HazardField.Rates(_profile, 0f, float.NaN, 1f));
        Assert.Equal(zero, HazardField.Rates(_profile, 0f, float.PositiveInfinity, 1f));
        Assert.Equal(zero, HazardField.Rates(_profile, 0f, 0f, float.NaN));
        Assert.Equal(zero, HazardField.Rates(_profile, 0f, 0f, float.PositiveInfinity));

        // The accepted counterpart: finite inputs give the night rates.
        Assert.Equal(_profile.Night, HazardField.Rates(_profile, 0f, 0f, 1f));

        var up = new Vector3(0f, 1f, 0f);
        Assert.Equal(0f, HazardField.Daylight(up, new Vector3(float.NaN, 1f, 0f), 0.1f));
        Assert.Equal(0f, HazardField.Daylight(new Vector3(float.PositiveInfinity, 0f, 0f), up, 0.1f));
    }

    [Fact]
    public void HasAtmosphere_VacuumZero()
    {
        Assert.True(HazardField.HasAtmosphere(new HazardRates(0.4f, 0.3f, 0.4f, 0.2f, 0f)));
        Assert.True(HazardField.HasAtmosphere(default));
        Assert.False(HazardField.HasAtmosphere(new HazardRates(0f, 0f, 0f, 0f, 0.5f)));
        Assert.False(HazardField.HasAtmosphere(new HazardRates(0.4f, 0.3f, 0.4f, 0.2f, 0.01f)));
    }
}
