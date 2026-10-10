using Horyzont.Core.Ids;

namespace Horyzont.Core.Frames;

/// <summary>Range of single-precision local coordinates per frame kind (K5-20, decision 3; P2).</summary>
public static class FrameLimits
{
    /// <summary>Largest coordinate in a planet frame (metres); equal to <c>PlanetFrames.MaxCoordinate</c> of the game.</summary>
    public const double PlanetMaxCoordinate = 100_000;

    /// <summary>Largest coordinate in a construct frame (metres).</summary>
    public const double ConstructMaxCoordinate = 100_000;

    /// <summary>Largest coordinate in the system frame (metres); float resolution there is at most 3.1 cm.</summary>
    public const double SystemMaxCoordinate = 500_000;

    /// <summary>Largest local coordinate in a frame of <paramref name="kind"/>; 0 for an unknown kind.</summary>
    /// <param name="kind">The frame kind.</param>
    /// <returns>The limit in metres.</returns>
    public static double MaxCoordinate(FrameKind kind) => kind switch
    {
        FrameKind.Planet => PlanetMaxCoordinate,
        FrameKind.Construct => ConstructMaxCoordinate,
        FrameKind.System => SystemMaxCoordinate,
        _ => 0,
    };
}
