using Horyzont.Bots.Scripting;
using Horyzont.Core.Simulation;

namespace Horyzont.Bots.Tests;

public sealed class BotScriptTests
{
    private const int TenMinutes = 36_000;

    [Fact]
    public void Script_SameSeedAndRole_ProducesSameCommandStream()
    {
        var first = Stream(new BotScript(42, BotRole.Builder, new BotSchedule()), TenMinutes);
        var second = Stream(new BotScript(42, BotRole.Builder, new BotSchedule()), TenMinutes);

        Assert.Equal(first, second);
        Assert.NotEqual(first, Stream(new BotScript(43, BotRole.Builder, new BotSchedule()), TenMinutes));
        Assert.NotEqual(first, Stream(new BotScript(42, BotRole.Explorer, new BotSchedule()), TenMinutes));
        Assert.NotEqual(first, Stream(new BotScript(42, BotRole.Abuser, new BotSchedule()), TenMinutes));
    }

    [Fact]
    public void Script_BuilderNormalMode_StaysWithinGameRulesPacing()
    {
        var rules = new GameRules();
        var schedule = new BotSchedule();
        var script = new BotScript(7, BotRole.Builder, schedule);
        var terrain = new List<long>();
        var blocks = new List<long>();
        for (long tick = 0; tick < TenMinutes; tick++)
        {
            if (schedule.PhaseAt(tick) != BotPhase.Normal)
            {
                continue;
            }

            switch (script.At(tick).Action)
            {
                case BotAction.Dig or BotAction.Fill:
                    terrain.Add(tick);
                    break;
                case BotAction.PlaceBlock or BotAction.RemoveBlock:
                    blocks.Add(tick);
                    break;
                default:
                    break;
            }
        }

        // Roughly 1 terrain edit per second and 1 block placement every other second in normal mode, well under the limits.
        Assert.InRange(terrain.Count, 500, 700);
        Assert.InRange(blocks.Count, 250, 350);
        Assert.True(MaxInAnyWindow(terrain, BotSchedule.TicksPerSecond) <= rules.TerrainEditsPerSecond);
        Assert.True(MaxInAnyWindow(blocks, BotSchedule.TicksPerSecond) <= rules.BlockEditsPerSecond);
    }

    [Fact]
    public void Script_BuilderBurst_EditsFasterThanNormalButNotBeyondTheSteadyLimits()
    {
        var rules = new GameRules();
        var schedule = new BotSchedule();
        var script = new BotScript(7, BotRole.Builder, schedule);
        var start = schedule.BurstStartTicks;
        Assert.Equal(BotPhase.Burst, schedule.PhaseAt(start));
        Assert.Equal(BotPhase.Normal, schedule.PhaseAt(start + schedule.BurstTicks));

        var terrain = 0;
        var blocks = 0;
        for (var tick = start; tick < start + schedule.BurstTicks; tick++)
        {
            switch (script.At(tick).Action)
            {
                case BotAction.Dig or BotAction.Fill:
                    terrain++;
                    break;
                case BotAction.PlaceBlock or BotAction.RemoveBlock:
                    blocks++;
                    break;
                default:
                    break;
            }
        }

        var seconds = schedule.BurstTicks / BotSchedule.TicksPerSecond;
        Assert.True(terrain >= 4 * seconds);
        Assert.True(terrain <= rules.TerrainEditsPerSecond * seconds);
        Assert.True(blocks >= 5 * seconds);
        Assert.True(blocks <= rules.BlockEditsPerSecond * seconds);
    }

    [Fact]
    public void Schedule_ControlPeriod_EndsWithQuietThenDrain()
    {
        var schedule = new BotSchedule();
        var period = schedule.ControlPeriodTicks;

        Assert.Equal(BotPhase.Normal, schedule.PhaseAt(period - schedule.QuietTicks - schedule.DrainTicks - 1));
        Assert.Equal(BotPhase.Quiet, schedule.PhaseAt(period - schedule.QuietTicks - schedule.DrainTicks));
        Assert.Equal(BotPhase.Quiet, schedule.PhaseAt(period - schedule.DrainTicks - 1));
        Assert.Equal(BotPhase.Drain, schedule.PhaseAt(period - schedule.DrainTicks));
        Assert.Equal(BotPhase.Drain, schedule.PhaseAt(period - 1));
        Assert.Equal(BotPhase.Normal, schedule.PhaseAt(period));
        Assert.Equal(BotPhase.Normal, schedule.PhaseAt(-5));
        Assert.Equal(15 * 60 * 60, period);
    }

    [Theory]
    [InlineData(BotRole.Builder)]
    [InlineData(BotRole.Explorer)]
    [InlineData(BotRole.Abuser)]
    public void Script_QuietAndDrain_SendNoIntentsAndDrainStandsStill(BotRole role)
    {
        var schedule = new BotSchedule();
        var script = new BotScript(11, role, schedule);
        var end = schedule.ControlPeriodTicks;

        for (var tick = end - schedule.QuietTicks - schedule.DrainTicks; tick < end; tick++)
        {
            var command = script.At(tick);
            Assert.Equal(BotAction.None, command.Action);
            if (schedule.PhaseAt(tick) == BotPhase.Drain)
            {
                Assert.Equal(0f, command.MoveX);
                Assert.Equal(0f, command.MoveY);
                Assert.False(command.Jump);
            }
        }
    }

    [Fact]
    public void Script_Abuser_SendsOnlyAbuseActionsInSeriesEveryTenSeconds()
    {
        var script = new BotScript(5, BotRole.Abuser, new BotSchedule());
        var kinds = new HashSet<BotAction>();
        var abuseTicks = 0;
        for (long tick = 0; tick < 600; tick++)
        {
            var action = script.At(tick).Action;
            if (action != BotAction.None)
            {
                kinds.Add(action);
                abuseTicks++;
            }
        }

        Assert.Equal(
            [BotAction.AbuseOutOfReach, BotAction.AbuseBadRadius, BotAction.AbuseNotFinite, BotAction.AbuseTooFast],
            kinds.Order().ToArray());
        Assert.InRange(abuseTicks, 10, 80);
        Assert.Equal(script.At(10).Action, script.At(610).Action);
    }

    [Fact]
    public void Script_Explorer_WalksOutAndBackAlongOneHeading()
    {
        var script = new BotScript(3, BotRole.Explorer, new BotSchedule());
        var outbound = script.At(100);
        var inbound = script.At(BotScript.ExplorerLegTicks + 100);

        Assert.Equal(1f, outbound.MoveY);
        Assert.True(outbound.Sprint);
        Assert.Equal(1f, inbound.MoveY);
        var turn = Math.Abs(Math.IEEERemainder(inbound.Yaw - outbound.Yaw, 2 * Math.PI));
        Assert.Equal(Math.PI, turn, 3);
    }

    [Fact]
    public void Script_AnyRole_YawStaysInRangeAndAxesAreBounded()
    {
        foreach (var role in Enum.GetValues<BotRole>())
        {
            var script = new BotScript(99, role, new BotSchedule());
            for (long tick = 0; tick < 100_000; tick += 7)
            {
                var c = script.At(tick);
                Assert.InRange(c.Yaw, -MathF.PI - 0.0001f, MathF.PI + 0.0001f);
                Assert.InRange(c.MoveX, -1f, 1f);
                Assert.InRange(c.MoveY, -1f, 1f);
                Assert.True(float.IsFinite(c.Pitch));
            }
        }
    }

    private static List<BotCommand> Stream(BotScript script, int ticks)
    {
        var commands = new List<BotCommand>(ticks);
        for (var tick = 0; tick < ticks; tick++)
        {
            commands.Add(script.At(tick));
        }

        return commands;
    }

    private static int MaxInAnyWindow(List<long> ticks, int window)
    {
        var max = 0;
        var left = 0;
        for (var right = 0; right < ticks.Count; right++)
        {
            while (ticks[right] - ticks[left] >= window)
            {
                left++;
            }

            max = Math.Max(max, right - left + 1);
        }

        return max;
    }
}
