namespace Horyzont.Bots.Scripting;

/// <summary>
/// The behaviour of one bot as a pure function of its seed, role, schedule and the tick counted from the shared epoch: the same inputs
/// always give the same command, so a run can be repeated and two processes agree without talking. The script does not know where the
/// bot is; the walks are closed loops (out and back along one heading) that keep a builder in the shared zone and an explorer on its
/// round trip. Client side only: randomness never reaches the application of operations.
/// </summary>
public sealed class BotScript
{
    /// <summary>Ticks a builder keeps one heading (4 s, about 30 m sprinting).</summary>
    public const int BuilderSegmentTicks = 240;

    /// <summary>Distance of a builder's work site from home in metres.</summary>
    public const float SiteDistanceMeters = 25f;

    /// <summary>Distance from its site in metres beyond which a builder turns back (zone: 25 + 35 + one segment of 30 m stays within 100 m).</summary>
    public const float ZoneRadiusMeters = 35f;

    private const int SiteTravelTicks = 200;

    /// <summary>Ticks of the outbound leg of an explorer: 1.5 km at the sprint speed of 7.5 m/s.</summary>
    public const int ExplorerLegTicks = 12_000;

    private const float ToolPitch = -0.7f;
    private const int AbuserPeriodTicks = 600;

    private readonly ulong _seed;
    private readonly BotRole _role;
    private readonly BotSchedule _schedule;
    private ulong _headingSegment;
    private ulong _latchedSegment;
    private float _latchedTravel;
    private bool _latched;
    private float _heading;

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
        _heading = BotRandom.Yaw(_seed, 0xBEEF);
    }

    /// <summary>Gets the command for a tick.</summary>
    /// <param name="tick">Ticks since the epoch shared by all bots.</param>
    /// <returns>The command.</returns>
    public BotCommand At(long tick) => At(tick, default);

    /// <summary>Gets the command for a tick, given what the bot knows about where it is.</summary>
    /// <param name="tick">Ticks since the epoch shared by all bots.</param>
    /// <param name="sense">Direction and distance to the home of the bot.</param>
    /// <returns>The command.</returns>
    public BotCommand At(long tick, in BotSense sense)
    {
        var phase = _schedule.PhaseAt(tick);
        var command = _role switch
        {
            BotRole.Builder => Builder(tick, phase == BotPhase.Burst, sense),
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

    private BotCommand Builder(long tick, bool burst, in BotSense sense)
    {
        // A builder first walks to a work site of its own (SiteDistanceMeters from home, in a direction spread over the circle by its
        // seed, so eight builders do not crowd one spot), then sprints along a meandering heading and turns back to the site
        // when it has strayed. It strafes, so it faces the ground beside its path and the line of sight of a dig never crosses a
        // block it placed itself; fresh ground keeps its blocks out of its own way.
        var siteYaw = Wrap((float)(((_seed * 0.6180339887498949) % 1.0) * 2.0 * Math.PI) - MathF.PI);
        float travel;
        if (tick < SiteTravelTicks)
        {
            travel = siteYaw;
        }
        else
        {
            // The decision to turn back is taken once per segment and kept for the whole segment, so a builder on the edge of its
            // zone walks home instead of dithering on the border. Home is at (cos, sin) * distance in the tangent plane, the site at
            // SiteDistanceMeters in the direction siteYaw.
            var segment = (ulong)(tick / BuilderSegmentTicks);
            if (!_latched || segment != _latchedSegment)
            {
                _latched = true;
                _latchedSegment = segment;
                var toSiteX = (SiteDistanceMeters * MathF.Cos(siteYaw)) + (sense.DistanceHome * MathF.Cos(sense.YawToHome));
                var toSiteY = (SiteDistanceMeters * MathF.Sin(siteYaw)) + (sense.DistanceHome * MathF.Sin(sense.YawToHome));
                _latchedTravel = (toSiteX * toSiteX) + (toSiteY * toSiteY) > ZoneRadiusMeters * ZoneRadiusMeters
                    ? MathF.Atan2(toSiteY, toSiteX)
                    : MeanderHeading(segment);
            }

            travel = _latchedTravel;
        }

        var facing = Wrap(travel + (MathF.PI / 2f));
        var action = tick < SiteTravelTicks ? BotAction.None : burst ? BurstAction(tick) : NormalAction(tick);
        var jump = tick % 900 == 450;
        return new BotCommand(1f, 0f, facing, ToolPitch, jump, true, action);
    }

    /// <summary>
    /// The heading of a segment: the previous heading turned by 30 to 100 degrees to a random side, so a builder keeps moving on
    /// fresh ground instead of doubling back. Computed incrementally; a jump in time recomputes from the first segment.
    /// </summary>
    private float MeanderHeading(ulong segment)
    {
        if (segment < _headingSegment)
        {
            _headingSegment = 0;
            _heading = BotRandom.Yaw(_seed, 0xBEEF);
        }

        while (_headingSegment < segment)
        {
            _headingSegment++;
            var mix = BotRandom.Mix(_seed, _headingSegment, 0x7A57);
            var turn = (30f + (mix % 71UL)) * (MathF.PI / 180f);
            _heading = Wrap(_heading + (((mix >> 32) & 1UL) == 0UL ? turn : -turn));
        }

        return _heading;
    }

    private static float Wrap(float yaw)
    {
        var wrapped = MathF.IEEERemainder(yaw, 2f * MathF.PI);
        return wrapped <= -MathF.PI ? wrapped + (2f * MathF.PI) : wrapped;
    }

    private static BotAction NormalAction(long tick)
    {
        // One terrain edit per second and one block placement every other second (the removals that would make up the second block operation need the construct mirror of K3-15), far under the limits of the rules.
        var second = tick / BotSchedule.TicksPerSecond;
        return (int)(tick % BotSchedule.TicksPerSecond) switch
        {
            0 => (second & 1) == 0 ? BotAction.Dig : BotAction.Fill,
            30 when (second & 1) == 0 => BotAction.PlaceBlock,
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
            <= 5 => BotAction.AbuseBadRadius,
            <= 8 => BotAction.AbuseNotFinite,
            >= 10 and <= 39 => BotAction.AbuseTooFast,
            _ => BotAction.None,
        };

        return new BotCommand(0f, 0f, yaw, ToolPitch, false, false, action);
    }
}
