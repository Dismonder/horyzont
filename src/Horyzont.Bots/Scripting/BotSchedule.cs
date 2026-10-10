namespace Horyzont.Bots.Scripting;

/// <summary>
/// Shared timetable of all bots, counted in ticks from a common epoch: a burst window in every burst period and, at the end of every
/// control period, a quiet window (no intents) followed by a drain window (no intents, no movement). The dump of the state is taken
/// at the end of the control period, so the break itself is never the moment of an edit.
/// </summary>
public sealed record BotSchedule
{
    /// <summary>Ticks per second the schedule is expressed in.</summary>
    public const int TicksPerSecond = 60;

    /// <summary>Gets the length of a control period (default 15 minutes).</summary>
    public long ControlPeriodTicks { get; init; } = 15L * 60 * TicksPerSecond;

    /// <summary>Gets the length of the quiet window before the end of a control period (default 60 s).</summary>
    public long QuietTicks { get; init; } = 60L * TicksPerSecond;

    /// <summary>Gets the length of the drain window at the end of a control period (default 20 s).</summary>
    public long DrainTicks { get; init; } = 20L * TicksPerSecond;

    /// <summary>Gets the length of a burst period (default 5 minutes).</summary>
    public long BurstPeriodTicks { get; init; } = 5L * 60 * TicksPerSecond;

    /// <summary>Gets the tick within a burst period at which the burst starts (default 2 minutes).</summary>
    public long BurstStartTicks { get; init; } = 2L * 60 * TicksPerSecond;

    /// <summary>Gets the length of a burst (default 10 s).</summary>
    public long BurstTicks { get; init; } = 10L * TicksPerSecond;

    /// <summary>Gets the phase at a tick counted from the epoch; ticks before the epoch are <see cref="BotPhase.Normal"/>.</summary>
    /// <param name="tick">Ticks since the epoch.</param>
    /// <returns>The phase.</returns>
    public BotPhase PhaseAt(long tick)
    {
        if (tick < 0)
        {
            return BotPhase.Normal;
        }

        var inControl = tick % ControlPeriodTicks;
        var untilEnd = ControlPeriodTicks - inControl;
        if (untilEnd <= DrainTicks)
        {
            return BotPhase.Drain;
        }

        if (untilEnd <= DrainTicks + QuietTicks)
        {
            return BotPhase.Quiet;
        }

        var inBurst = tick % BurstPeriodTicks;
        return inBurst >= BurstStartTicks && inBurst < BurstStartTicks + BurstTicks ? BotPhase.Burst : BotPhase.Normal;
    }
}
