using System.Numerics;

using Horyzont.Core.Content;

namespace Horyzont.Core.Survival;

/// <summary>The hazards at a point as pure functions of the profile, the height, the time of day and the intensity (K6-34, D9, D10); allocates nothing.</summary>
public static class HazardField
{
    /// <summary>
    /// The rates at a point: the day and night rates of the profile blended by <paramref name="daylight"/> (<c>night + (day - night) * daylight</c>), multiplied by the scale of the
    /// last zone whose <see cref="HazardZoneDef.MinHeight"/> is at most <paramref name="heightAboveRadius"/> (scale 1 when there is none) and by <paramref name="intensity"/>.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="heightAboveRadius">Height above the planet radius in metres.</param>
    /// <param name="daylight">0 at night, 1 by day.</param>
    /// <param name="intensity">The world intensity, 0.5 to 2 (values outside are clamped).</param>
    /// <returns>The rates; a non-finite input gives zero rates.</returns>
    public static HazardRates Rates(HazardProfileDef profile, float heightAboveRadius, float daylight, float intensity) => throw new NotImplementedException("K6-34 A6");

    /// <summary>The daylight at a point: <c>clamp((dot(up, sun) + band) / (2 * band), 0, 1)</c>.</summary>
    /// <param name="up">The unit up direction at the point.</param>
    /// <param name="sun">The unit direction to the sun.</param>
    /// <param name="band">The half width of the twilight band.</param>
    /// <returns>0 to 1; 0 for a non-finite input.</returns>
    public static float Daylight(Vector3 up, Vector3 sun, float band) => throw new NotImplementedException("K6-34 A6");

    /// <summary>Whether the place has an atmosphere: no vacuum among the present hazards.</summary>
    /// <param name="present">The rates before protection.</param>
    /// <returns>True when the vacuum rate is zero.</returns>
    public static bool HasAtmosphere(in HazardRates present) => throw new NotImplementedException("K6-34 A6");
}
