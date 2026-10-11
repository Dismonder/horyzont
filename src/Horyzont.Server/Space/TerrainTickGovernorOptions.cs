namespace Horyzont.Server.Space;

/// <summary>Settings of <see cref="TerrainTickGovernor"/> (K5-22 decision 7, P6).</summary>
public sealed record TerrainTickGovernorOptions
{
    /// <summary>Gets the window of the average tick time in seconds.</summary>
    public int AverageWindowSeconds { get; init; } = 5;

    /// <summary>Gets the average tick time above which terrain updates are throttled, in milliseconds.</summary>
    public double OverBudgetMilliseconds { get; init; } = 8.0;

    /// <summary>Gets the average tick time below which throttling may end, in milliseconds.</summary>
    public double RestoreMilliseconds { get; init; } = 6.0;

    /// <summary>Gets how long the average must stay below <see cref="RestoreMilliseconds"/> before throttling ends, in seconds.</summary>
    public int RestoreAfterSeconds { get; init; } = 10;

    /// <summary>Gets the ticks between two viewer updates of a throttled planet (every 4th tick).</summary>
    public int ThrottledViewerIntervalTicks { get; init; } = 4;

    /// <summary>Gets the seconds between two per-terrain log rounds while over budget.</summary>
    public int LogIntervalSeconds { get; init; } = 10;
}
