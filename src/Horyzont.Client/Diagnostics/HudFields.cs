namespace Horyzont.Client.Diagnostics;

/// <summary>Metrics with available samples.</summary>
[Flags]
public enum HudFields : ushort
{
    /// <summary>No samples.</summary>
    None = 0,
    /// <summary>Render frame rate.</summary>
    Fps = 1,
    /// <summary>Authoritative tick number.</summary>
    ServerTick = 2,
    /// <summary>Measured local server tick duration.</summary>
    ServerTickMilliseconds = 4,
    /// <summary>Remote round trip time.</summary>
    Rtt = 8,
    /// <summary>Transport byte rate.</summary>
    Bandwidth = 16,
    /// <summary>Tool pending and rejection counts.</summary>
    Tools = 32,
    /// <summary>Confirmed availability status.</summary>
    ReadOnly = 64,
}
