namespace Horyzont.Client.Dev;

/// <summary>Speed of the free developer camera, depending on the height above the surface.</summary>
public static class DevFlightSpeed
{
    /// <summary>Slowest speed in m/s (at the ground).</summary>
    public const float MinSpeed = 5f;

    /// <summary>Fastest speed in m/s (orbit).</summary>
    public const float MaxSpeed = 2000f;

    /// <summary>Speed gain per metre of height, per second.</summary>
    public const float PerMeterOfHeight = 0.5f;

    /// <summary>Speed for a height: clamp(height * 0.5 per second, 5, 2000) m/s. The scroll wheel multiplies the result.</summary>
    /// <param name="heightAboveSurfaceMeters">Height above the planet radius; negative values count as the minimum speed.</param>
    /// <returns>The speed in m/s.</returns>
    public static float At(float heightAboveSurfaceMeters) =>
        float.IsFinite(heightAboveSurfaceMeters)
            ? Math.Clamp(heightAboveSurfaceMeters * PerMeterOfHeight, MinSpeed, MaxSpeed)
            : MinSpeed;
}
