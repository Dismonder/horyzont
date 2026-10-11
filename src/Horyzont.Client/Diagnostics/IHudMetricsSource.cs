namespace Horyzont.Client.Diagnostics;

/// <summary>Allocation-free main-thread metric reads; false indicates an unavailable value.</summary>
public interface IHudMetricsSource
{
    /// <summary>Reads the authoritative tick.</summary>
    bool TryGetServerTick(out ulong tick);
    /// <summary>Reads local tick duration, never substitutes RTT.</summary>
    bool TryGetServerTickMilliseconds(out double milliseconds);
    /// <summary>Reads remote client RTT.</summary>
    bool TryGetRtt(out double milliseconds);
    /// <summary>Reads cumulative transport counters.</summary>
    bool TryGetTransportBytes(out long sent, out long received);
    /// <summary>Reads pending and rejected intentions.</summary>
    bool TryGetTools(out int pending, out int rejections);
    /// <summary>Gets confirmed read-only state.</summary>
    bool IsReadOnly { get; }
    /// <summary>Gets the session generation, changed on reconnect.</summary>
    int SessionGeneration { get; }
}
