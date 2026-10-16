using Horyzont.Core.Survival;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-33, D14: the rested bonus lasts 8 minutes plus 1 minute per level of comfort.</summary>
public sealed class RestRulesTests
{
    private const int TickRate = 60;

    private static readonly VitalsRules _rules = VitalsRules.Default;

    [Fact]
    public void DurationTicks_ComfortZero_EightMinutes() =>
        Assert.Equal(8UL * 60 * TickRate, RestRules.DurationTicks(0, _rules, TickRate));

    [Fact]
    public void DurationTicks_ComfortThree_ElevenMinutes() =>
        Assert.Equal(11UL * 60 * TickRate, RestRules.DurationTicks(3, _rules, TickRate));

    [Fact]
    public void DurationTicks_ComfortAboveMax_Capped()
    {
        // The accepted counterpart: the maximum itself is 18 minutes.
        Assert.Equal(18UL * 60 * TickRate, RestRules.DurationTicks(10, _rules, TickRate));

        Assert.Equal(18UL * 60 * TickRate, RestRules.DurationTicks(11, _rules, TickRate));
        Assert.Equal(18UL * 60 * TickRate, RestRules.DurationTicks(255, _rules, TickRate));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void DurationTicks_TickRateNotPositive_Throws(int tickRate)
    {
        Assert.Equal(8UL * 60 * TickRate, RestRules.DurationTicks(0, _rules, TickRate));

        Assert.Throws<ArgumentOutOfRangeException>(() => RestRules.DurationTicks(0, _rules, tickRate));
    }

    [Fact]
    public void DurationTicks_FollowsTheTickRate() =>
        Assert.Equal(8UL * 60 * 20, RestRules.DurationTicks(0, _rules, 20));
}
