using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Space;

/// <summary>
/// Keeps the server tick in budget with several terrains (K5-22 decision 7, spec 02:211): when the average tick time over
/// <see cref="TerrainTickGovernorOptions.AverageWindowSeconds"/> exceeds <see cref="TerrainTickGovernorOptions.OverBudgetMilliseconds"/>,
/// planets with exactly one player switch to sparse updates (the viewer is moved every
/// <see cref="TerrainTickGovernorOptions.ThrottledViewerIntervalTicks"/>-th tick instead of following the body); throttling ends when
/// the average stays below <see cref="TerrainTickGovernorOptions.RestoreMilliseconds"/> for
/// <see cref="TerrainTickGovernorOptions.RestoreAfterSeconds"/>. While over budget, every
/// <see cref="TerrainTickGovernorOptions.LogIntervalSeconds"/> one log entry per terrain. The tick time comes from the caller (the
/// K4-19 <c>TickTimeRecorder</c> once merged). Pure; allocation-free.
/// </summary>
public sealed partial class TerrainTickGovernor
{
    private readonly double[] _samples;
    private readonly int _restoreTicks;
    private readonly ulong _logIntervalTicks;
    private readonly ILogger<TerrainTickGovernor> _logger;
    private int _next;
    private int _count;
    private double _sum;
    private int _belowFor;
    private ulong _ticks;
    private ulong _lastLog;
    private bool _logged;

    /// <summary>Initializes a new instance of the <see cref="TerrainTickGovernor"/> class.</summary>
    /// <param name="tickRate">Server ticks per second.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="options">Settings; defaults when null.</param>
    public TerrainTickGovernor(int tickRate, ILogger<TerrainTickGovernor> logger, TerrainTickGovernorOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tickRate, 0);
        ArgumentNullException.ThrowIfNull(logger);
        Options = options ?? new TerrainTickGovernorOptions();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Options.AverageWindowSeconds, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Options.ThrottledViewerIntervalTicks, 0);
        _samples = new double[tickRate * Options.AverageWindowSeconds];
        _restoreTicks = tickRate * Options.RestoreAfterSeconds;
        _logIntervalTicks = (ulong)(tickRate * Options.LogIntervalSeconds);
        _logger = logger;
    }

    /// <summary>Gets the settings.</summary>
    public TerrainTickGovernorOptions Options { get; }

    /// <summary>Gets a value indicating whether planets with one player are throttled.</summary>
    public bool Throttled { get; private set; }

    /// <summary>Gets the average tick time over the window, in milliseconds (0 before the first sample).</summary>
    public double AverageMilliseconds => _count == 0 ? 0 : _sum / _count;

    /// <summary>Gets a value indicating whether the last <see cref="Observe"/> opened a log round (call <see cref="LogTerrain"/> per terrain).</summary>
    public bool LogDue { get; private set; }

    /// <summary>Records the time of one server tick and updates the mode.</summary>
    /// <param name="tickMilliseconds">Duration of the tick in milliseconds.</param>
    public void Observe(double tickMilliseconds)
    {
        if (!double.IsFinite(tickMilliseconds) || tickMilliseconds < 0)
        {
            tickMilliseconds = 0;
        }

        _ticks++;
        if (_count == _samples.Length)
        {
            _sum -= _samples[_next];
        }
        else
        {
            _count++;
        }

        _samples[_next] = tickMilliseconds;
        _sum += tickMilliseconds;
        _next = (_next + 1) % _samples.Length;

        var average = AverageMilliseconds;
        var full = _count == _samples.Length;
        if (!Throttled && full && average > Options.OverBudgetMilliseconds)
        {
            Throttled = true;
            _belowFor = 0;
            LogThrottled(_logger, average);
        }
        else if (Throttled)
        {
            _belowFor = average < Options.RestoreMilliseconds ? _belowFor + 1 : 0;
            if (_belowFor >= _restoreTicks)
            {
                Throttled = false;
                LogRestored(_logger, average);
            }
        }

        var over = full && average > Options.OverBudgetMilliseconds;
        LogDue = over && (!_logged || _ticks - _lastLog >= _logIntervalTicks);
        if (LogDue)
        {
            _logged = true;
            _lastLog = _ticks;
        }
    }

    /// <summary>Gets a value indicating whether the viewers of a planet are updated on a tick.</summary>
    /// <param name="players">Players whose region is the planet.</param>
    /// <param name="tick">The tick.</param>
    /// <returns>False for a throttled planet (exactly one player) between its sparse updates.</returns>
    public bool ShouldUpdateViewers(int players, ulong tick) =>
        !Throttled || players != 1 || tick % (ulong)Options.ThrottledViewerIntervalTicks == 0;

    /// <summary>Writes the over-budget entry of one terrain (call for every active terrain when <see cref="LogDue"/>).</summary>
    /// <param name="stats">The statistics of the terrain.</param>
    public void LogTerrain(in PlanetTerrainStats stats) =>
        LogTerrainStats(_logger, stats.Planet.Value, stats.Players, stats.Viewers, stats.BlocksInMemory, stats.UpdateMilliseconds, AverageMilliseconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tick over budget ({Average:F2} ms average): terrain viewers of planets with one player update sparsely.")]
    private static partial void LogThrottled(ILogger logger, double average);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tick back in budget ({Average:F2} ms average): terrain updates every tick.")]
    private static partial void LogRestored(ILogger logger, double average);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Terrain of planet {Planet}: {Players} players, {Viewers} viewers, {Blocks} blocks in memory, update {Update:F2} ms (tick {Average:F2} ms average).")]
    private static partial void LogTerrainStats(ILogger logger, ulong planet, int players, int viewers, int blocks, double update, double average);
}
