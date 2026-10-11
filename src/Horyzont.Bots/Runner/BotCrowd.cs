using Horyzont.Bots.Client;
using Horyzont.Bots.Scripting;

namespace Horyzont.Bots.Runner;

/// <summary>
/// A crowd of bots on one clock: every call of <see cref="Pump"/> runs the 60 Hz frames that are due since the shared epoch, each
/// frame once and in order, for every bot. The runner process calls it from a loop on the system clock; tests call it on virtual time,
/// so both run the same code. After a long stall the crowd skips frames instead of running a burst (<see cref="MaxCatchUpFrames"/>).
/// </summary>
public sealed class BotCrowd
{
    /// <summary>Most frames run by one call of <see cref="Pump"/>; later ones are skipped.</summary>
    public const int MaxCatchUpFrames = 30;

    private readonly IReadOnlyList<BotClient> _bots;
    private readonly TimeProvider _time;
    private readonly DateTimeOffset _epoch;
    private long _nextFrame;

    /// <summary>Initializes a new instance of the <see cref="BotCrowd"/> class.</summary>
    /// <param name="bots">The bots.</param>
    /// <param name="time">The clock; its UTC time against <paramref name="epoch"/> gives the script tick.</param>
    /// <param name="epoch">The moment of script tick 0, shared by all processes of a run (the bots connect and wait before it).</param>
    public BotCrowd(IReadOnlyList<BotClient> bots, TimeProvider time, DateTimeOffset epoch)
    {
        ArgumentNullException.ThrowIfNull(bots);
        ArgumentNullException.ThrowIfNull(time);
        _bots = bots;
        _time = time;
        _epoch = epoch;
        _nextFrame = -1;
    }

    /// <summary>Gets the bots.</summary>
    public IReadOnlyList<BotClient> Bots => _bots;

    /// <summary>Gets the number of frames skipped after stalls.</summary>
    public long SkippedFrames { get; private set; }

    /// <summary>Gets the number of the last frame that ran; -1 before the epoch.</summary>
    public long LastFrame => _nextFrame - 1;

    /// <summary>Gets or sets the edit latency measurement; resolved once per second of frames when set.</summary>
    public EditLatencyTracker? Latency { get; set; }

    /// <summary>
    /// Gets or sets where the check breaks write their dumps (<c>break001/bot0.jsonl</c>, ...); null for none. A dump is written
    /// <see cref="DumpLeadTicks"/> before the end of every control period of <see cref="Schedule"/>, inside the drain window.
    /// </summary>
    public string? DumpDirectory { get; set; }

    /// <summary>Gets or sets the timetable of the check breaks.</summary>
    public BotSchedule Schedule { get; set; } = new();

    /// <summary>Gets the number of check breaks dumped.</summary>
    public int Breaks { get; private set; }

    /// <summary>Ticks before the end of a control period at which the bots dump their state (2 s; the orchestrator asks the server then).</summary>
    public const int DumpLeadTicks = 2 * BotSchedule.TicksPerSecond;

    /// <summary>Gets the index (from 1) of the check break dumped at a frame, or 0 when the frame is no dump frame.</summary>
    /// <param name="frame">Frame since the epoch.</param>
    /// <param name="schedule">Timetable.</param>
    /// <returns>The break index.</returns>
    public static long BreakAt(long frame, BotSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var next = frame + DumpLeadTicks;
        return frame >= 0 && next % schedule.ControlPeriodTicks == 0 ? next / schedule.ControlPeriodTicks : 0;
    }

    /// <summary>Runs the frames that are due.</summary>
    /// <returns>The number of frames run.</returns>
    public int Pump()
    {
        var elapsed = _time.GetUtcNow() - _epoch;
        var due = (long)Math.Floor(elapsed.TotalSeconds * BotSchedule.TicksPerSecond);
        if (due < 0)
        {
            // Before the epoch the bots only connect and wait.
            foreach (var bot in _bots)
            {
                bot.Tick(-1);
            }

            return 0;
        }

        if (_nextFrame < 0)
        {
            _nextFrame = 0;
        }

        if (due - _nextFrame >= MaxCatchUpFrames)
        {
            var skipTo = due - MaxCatchUpFrames + 1;
            SkippedFrames += skipTo - _nextFrame;
            _nextFrame = skipTo;
        }

        var run = 0;
        for (; _nextFrame <= due; _nextFrame++)
        {
            foreach (var bot in _bots)
            {
                bot.Tick(_nextFrame);
            }

            AfterFrame(_nextFrame);
            run++;
        }

        return run;
    }

    /// <summary>Writes the dumps of every bot for a check break.</summary>
    /// <param name="index">Break index (from 1).</param>
    public void Dump(long index)
    {
        if (DumpDirectory is null)
        {
            return;
        }

        var directory = Path.Combine(DumpDirectory, "break" + index.ToString("000", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        foreach (var bot in _bots)
        {
            using var writer = new StreamWriter(Path.Combine(directory, bot.Options.Name + ".jsonl"), append: false, new System.Text.UTF8Encoding(false));
            bot.WriteDump(writer);
        }

        Breaks++;
    }

    private void AfterFrame(long frame)
    {
        if (Latency is not null && frame % BotSchedule.TicksPerSecond == 0)
        {
            Latency.Resolve();
        }

        if (BreakAt(frame, Schedule) is var index and > 0)
        {
            Dump(index);
        }
    }
}
