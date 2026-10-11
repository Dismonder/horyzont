namespace Horyzont.Core.Space;

/// <summary>Constants of the zones of influence of the star system (K5-21 decision 4, spec 02:52 and :158).</summary>
public static class StarSystemRules
{
    /// <summary>A body's zone of influence is the ball of this many radii; a ship in the system frame enters a planet frame here.</summary>
    public const double ZoneRadiusFactor = 3.0;

    /// <summary>A ship in a planet frame leaves for the system frame beyond this many radii (hysteresis band 3.0–3.2 R).</summary>
    public const double ZoneExitFactor = 3.2;

    /// <summary>Gravity follows the inverse square up to this many radii, then fades linearly to zero at the zone radius.</summary>
    public const double GravityFadeStartFactor = 2.5;

    /// <summary>Minimum gap between the exit spheres (3.2 R) of any two bodies.</summary>
    public const double MinZoneGapMeters = 250;

    /// <summary>Most bodies a star system may have (blob 10 and message 32 limit).</summary>
    public const int MaxBodies = 8;

    /// <summary>The only star system of subprojects 1–6.</summary>
    public const ulong SystemIdValue = 1;
}
