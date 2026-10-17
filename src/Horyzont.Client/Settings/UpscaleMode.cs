namespace Horyzont.Client.Settings;

/// <summary>The upscaling technique the renderer uses when the render scale is below 1.</summary>
public enum UpscaleMode
{
    /// <summary>No upscaling: the scene is rendered at the window resolution (the render scale then has no effect).</summary>
    Off = 0,

    /// <summary>AMD FidelityFX Super Resolution 1.0: spatial, cheap, no motion vectors.</summary>
    Fsr1 = 1,

    /// <summary>AMD FidelityFX Super Resolution 2.2: temporal, needs the Forward+ renderer.</summary>
    Fsr2 = 2,
}
