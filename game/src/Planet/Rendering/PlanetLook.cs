namespace Horyzont.Game.Planet.Rendering;

/// <summary>
/// Visual dressing of a planet that is not part of any saved or simulated state: heights relative to the nominal radius where the
/// shader paints beaches, grass and snow, the visual sea level and where flora grows. Changing these values changes only how the
/// planet looks, never the terrain data (the generator profile owns the shape and the voxel materials).
/// </summary>
public static class PlanetLook
{
    /// <summary>Height of the water surface (metres above the nominal radius). Purely visual: the character walks on the sea floor.</summary>
    public const float SeaLevelMeters = -42f;

    /// <summary>Height above the sea that is painted as a beach.</summary>
    public const float BeachHeightMeters = 3.5f;

    /// <summary>Height above which flat ground is covered by snow.</summary>
    public const float SnowLineMeters = 135f;

    /// <summary>Height above which no trees grow.</summary>
    public const float TreeLineMeters = 110f;
}
