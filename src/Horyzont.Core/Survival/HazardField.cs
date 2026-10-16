using System.Numerics;

using Horyzont.Core.Content;

namespace Horyzont.Core.Survival;

/// <summary>The hazards at a point as pure functions of the profile, the height, the time of day and the intensity (K6-34, D9, D10); allocates nothing.</summary>
public static class HazardField
{
    private const float MinIntensity = 0.5f;
    private const float MaxIntensity = 2f;

    /// <summary>
    /// The rates at a point: the day and night rates of the profile blended by <paramref name="daylight"/> (<c>night + (day - night) * daylight</c>, evaluated as
    /// <c>day * daylight + night * (1 - daylight)</c> so that both ends are exact), multiplied by the scale of the
    /// last zone whose <see cref="HazardZoneDef.MinHeight"/> is at most <paramref name="heightAboveRadius"/> (scale 1 when there is none) and by <paramref name="intensity"/>.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="heightAboveRadius">Height above the planet radius in metres.</param>
    /// <param name="daylight">0 at night, 1 by day (values outside are clamped).</param>
    /// <param name="intensity">The world intensity, 0.5 to 2 (values outside are clamped).</param>
    /// <returns>The rates; a non-finite input gives zero rates.</returns>
    public static HazardRates Rates(HazardProfileDef profile, float heightAboveRadius, float daylight, float intensity)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!float.IsFinite(heightAboveRadius) || !float.IsFinite(daylight) || !float.IsFinite(intensity))
        {
            return default;
        }

        var day = Math.Clamp(daylight, 0f, 1f);
        var factor = Math.Clamp(intensity, MinIntensity, MaxIntensity);
        var zones = profile.Zones;
        var scale = new HazardRates(1f, 1f, 1f, 1f, 1f);
        for (var i = 0; i < zones.Count; i++)
        {
            if (zones[i].MinHeight <= heightAboveRadius)
            {
                scale = zones[i].Scale;
            }
        }

        return new HazardRates(
            Blend(profile.Day.Cold, profile.Night.Cold, day) * scale.Cold * factor,
            Blend(profile.Day.Heat, profile.Night.Heat, day) * scale.Heat * factor,
            Blend(profile.Day.Toxins, profile.Night.Toxins, day) * scale.Toxins * factor,
            Blend(profile.Day.Radiation, profile.Night.Radiation, day) * scale.Radiation * factor,
            Blend(profile.Day.Vacuum, profile.Night.Vacuum, day) * scale.Vacuum * factor);
    }

    /// <summary>The daylight at a point: <c>clamp((dot(up, sun) + band) / (2 * band), 0, 1)</c>.</summary>
    /// <param name="up">The unit up direction at the point.</param>
    /// <param name="sun">The unit direction to the sun.</param>
    /// <param name="band">The half width of the twilight band.</param>
    /// <returns>0 to 1; 0 for a non-finite input.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="band"/> is not a positive finite number.</exception>
    public static float Daylight(Vector3 up, Vector3 sun, float band)
    {
        if (!float.IsFinite(band) || band <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(band), band, "The twilight band must be positive.");
        }

        var dot = Vector3.Dot(up, sun);
        return float.IsFinite(dot) ? Math.Clamp((dot + band) / (2f * band), 0f, 1f) : 0f;
    }

    /// <summary>Whether the place has an atmosphere: no vacuum among the present hazards.</summary>
    /// <param name="present">The rates before protection.</param>
    /// <returns>True when the vacuum rate is zero.</returns>
    public static bool HasAtmosphere(in HazardRates present) => !(present.Vacuum > 0f);

    private static float Blend(float day, float night, float daylight) => (day * daylight) + (night * (1f - daylight));
}
