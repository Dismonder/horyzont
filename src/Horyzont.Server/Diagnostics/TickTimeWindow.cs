namespace Horyzont.Server.Diagnostics;

/// <summary>Statistics of the server tick durations recorded since the previous window was taken.</summary>
/// <param name="Count">Number of ticks in the window.</param>
/// <param name="MeanMs">Mean duration in milliseconds (exact).</param>
/// <param name="P95Ms">95th percentile in milliseconds (histogram resolution, never above the maximum).</param>
/// <param name="P99Ms">99th percentile in milliseconds (histogram resolution, never above the maximum).</param>
/// <param name="MaxMs">Longest tick in milliseconds (exact).</param>
public readonly record struct TickTimeWindow(int Count, double MeanMs, double P95Ms, double P99Ms, double MaxMs);
