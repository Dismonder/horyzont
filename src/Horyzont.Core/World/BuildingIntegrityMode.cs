namespace Horyzont.Core.World;

/// <summary>How strictly the integrity of buildings is judged (K6-35 D4, read by K6-36).</summary>
public enum BuildingIntegrityMode : byte
{
    /// <summary>Full support rules.</summary>
    On = 1,

    /// <summary>Relaxed support rules.</summary>
    Relaxed = 2,

    /// <summary>No support rules.</summary>
    Off = 3,
}
