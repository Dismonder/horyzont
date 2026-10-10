namespace Horyzont.Bots.Scripting;

/// <summary>
/// The behaviour of one bot as a pure function of its seed, role, schedule and the tick counted from the shared epoch: the same inputs
/// always give the same command, so a run can be repeated and two processes agree without talking. The script does not know where the
/// bot is; the walks are closed loops (out and back along one heading) that keep a builder in the shared zone and an explorer on its
/// round trip. Client side only: randomness never reaches the application of operations.
/// </summary>
public sealed class BotScript
{
    /// <summary>Ticks of one walking leg of a builder (5 s, about 22 m on foot); the way back takes as long.</summary>
    public const int BuilderLegTicks = 300;

    /// <summary>Ticks of the outbound leg of an explorer: 1.5 km at the sprint speed of 7.5 m/s.</summary>
    public const int ExplorerLegTicks = 12_000;

    private const float ToolPitch = -0.7f;
    private const int AbuserPeriodTicks = 600;

    private readonly ulong _seed;
    private readonly BotRole _role;
    private readonly BotSchedule _schedule;

    /// <summary>Initializes a new instance of the <see cref="BotScript"/> class.</summary>
    /// <param name="seed">Seed of the bot; bots with different seeds walk different headings.</param>
    /// <param name="role">The role.</param>
    /// <param name="schedule">The shared timetable.</param>
    public BotScript(ulong seed, BotRole role, BotSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        _seed = seed ^ ((ulong)role << 56);
        _role = role;
        _schedule = schedule;
    }

    /// <summary>Gets the command for a tick.</summary>
    /// <param name="tick">Ticks since the epoch shared by all bots.</param>
    /// <returns>The command.</returns>
    public BotCommand At(long tick)
    {
        var phase = _schedule.PhaseAt(tick);
        var command = _role switch
        {
            BotRole.Builder => Builder(tick, phase == BotPhase.Burst),
            BotRole.Explorer => Explorer(tick),
            _ => Abuser(tick),
        };

        return phase switch
        {
            BotPhase.Quiet => command with { Action = BotAction.None },
            BotPhase.Drain => BotCommand.Idle(command.Yaw),
            _ => command,
        };
    }

    private static float Reverse(float yaw) => yaw > 0f ? yaw - MathF.PI : yaw + MathF.PI;

    private BotCommand Builder(long tick, bool burst)
    {
        var leg = tick / (2 * BuilderLegTicks);
        var inLeg = (int)(tick % (2 * BuilderLegTicks));
        var heading = BotRandom.Yaw(_seed, (ulong)leg);
        var yaw = inLeg < BuilderLegTicks ? heading : Reverse(heading);
        var action = burst ? BurstAction(tick) : NormalAction(tick);
        var jump = tick % 900 == 450;
        return new BotCommand(0f, 1f, yaw, ToolPitch, jump, false, action);
    }

    private static BotAction NormalAction(long tick)
    {
        // About one terrain edit and two block placements per second, far under the limits of the rules.
        var second = tick / BotSchedule.TicksPerSecond;
        return (int)(tick % BotSchedule.TicksPerSecond) switch
        {
            0 => (second & 1) == 0 ? BotAction.Dig : BotAction.Fill,
            20 or 40 => BotAction.PlaceBlock,
            _ => BotAction.None,
        };
    }

    private static BotAction BurstAction(long tick)
    {
        // 5 terrain edits and 7.5 block placements per second: close to the limits of the rules, never over them.
        var slot = (int)(tick % 12);
        if (slot == 0)
        {
            return ((tick / 12) & 1) == 0 ? BotAction.Dig : BotAction.Fill;
        }

        if (slot == 6 || (tick % 24) == 3)
        {
            return BotAction.PlaceBlock;
        }

        return BotAction.None;
    }

    private BotCommand Explorer(long tick)
    {
        var loop = tick / (2 * ExplorerLegTicks);
        var inLoop = tick % (2 * ExplorerLegTicks);
        var heading = BotRandom.Yaw(_seed, (ulong)loop);
        var yaw = inLoop < ExplorerLegTicks ? heading : Reverse(heading);
        var action = tick % 300 == 150 ? BotAction.Dig : BotAction.None;
        return new BotCommand(0f, 1f, yaw, ToolPitch, tick % 1200 == 600, true, action);
    }

    private BotCommand Abuser(long tick)
    {
        var yaw = BotRandom.Yaw(_seed, (ulong)(tick / AbuserPeriodTicks));
        var slot = (int)(tick % AbuserPeriodTicks);
        var action = slot switch
        {
            <= 2 => BotAction.AbuseOutOfReach,
            <= 5 => BotAction.AbuseUnderCore,
            <= 8 => BotAction.AbuseNotFinite,
            >= 10 and <= 39 => BotAction.AbuseTooFast,
            _ => BotAction.None,
        };

        return new BotCommand(0f, 0f, yaw, ToolPitch, false, false, action);
    }
}
