namespace Horyzont.Core.Space;

/// <summary>Ranges of one generated body of a <see cref="StarSystemLayout"/> (K5-21 decision 3).</summary>
/// <param name="MinRadiusMeters">Smallest radius; radii are quantised to 10 m.</param>
/// <param name="MaxRadiusMeters">Largest radius.</param>
/// <param name="MinGravityCentis">Smallest surface gravity in hundredths of m/s².</param>
/// <param name="MaxGravityCentis">Largest surface gravity in hundredths of m/s².</param>
/// <param name="AtmosphereHeightMeters">Atmosphere height (above the maximum relief, decision 6).</param>
/// <param name="CoreDepthMeters">Core depth.</param>
/// <param name="MinDistanceMeters">Smallest distance from the home planet before the separation rule; quantised to 1 m.</param>
/// <param name="MaxDistanceMeters">Largest distance from the home planet before the separation rule.</param>
public sealed record BodyLayout(
    int MinRadiusMeters,
    int MaxRadiusMeters,
    int MinGravityCentis,
    int MaxGravityCentis,
    int AtmosphereHeightMeters,
    int CoreDepthMeters,
    int MinDistanceMeters,
    int MaxDistanceMeters);
