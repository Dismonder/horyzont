using Horyzont.Client.Survival;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Progress;

namespace Horyzont.Client.Tests.Survival;

/// <summary>The numbers of the vitals HUD as text (K6-33): rounded down, minutes of the food and of the rest.</summary>
public sealed class VitalsHudTextTests
{
    private static readonly FoodDef _berries = new(10, 20, 0.5f, 600);
    private const int TickRate = 60;

    [Fact]
    public void FoodRemaining_Minutes()
    {
        var slot = new FoodSlot(new ItemTypeId(5), new Tick(1_000));

        // 600 s = 36 000 ticks; after 12 000 ticks 400 s (6 min 40 s) are left, shown as 6 min.
        var left = VitalsHudText.FoodRemainingTicks(slot, _berries, new Tick(13_000), TickRate);

        Assert.Equal(24_000UL, left);
        Assert.Equal("6 min", VitalsHudText.Duration(left, TickRate));
        Assert.Equal(24_000.0 / 36_000.0, VitalsHudText.FoodFraction(slot, _berries, new Tick(13_000), TickRate), 6);
        Assert.Equal("10 min", VitalsHudText.Duration(VitalsHudText.FoodRemainingTicks(slot, _berries, new Tick(1_000), TickRate), TickRate));
        Assert.Equal(0UL, VitalsHudText.FoodRemainingTicks(slot, _berries, new Tick(1_000 + 36_000), TickRate));
        Assert.Equal(0UL, VitalsHudText.FoodRemainingTicks(slot, _berries, new Tick(900_000), TickRate));
        Assert.Equal(0UL, VitalsHudText.FoodRemainingTicks(default, _berries, new Tick(5), TickRate));
        Assert.Equal(0UL, VitalsHudText.FoodRemainingTicks(slot, null, new Tick(5), TickRate));
        Assert.Equal(0.0, VitalsHudText.FoodFraction(default, _berries, new Tick(5), TickRate));
    }

    [Fact]
    public void RestRemaining_MinutesAndComfort()
    {
        // (8 + 3) minutes of rest at 60 ticks per second.
        Assert.Equal("11 min", VitalsHudText.Duration(11UL * 3600UL, TickRate));
        Assert.Equal("10 min", VitalsHudText.Duration((10UL * 3600UL) + 3599UL, TickRate));
        Assert.Equal("59 s", VitalsHudText.Duration((59UL * TickRate) + 59UL, TickRate));
        Assert.Equal("0 s", VitalsHudText.Duration(0, TickRate));
        Assert.Equal("1 min", VitalsHudText.Duration(60UL * TickRate, TickRate));
        Assert.Equal("2 min", VitalsHudText.Duration(120UL * 30UL, 30));
    }

    [Fact]
    public void Values_RoundedDownToWhole()
    {
        Assert.Equal("85 / 110", VitalsHudText.Pair(85.99f, 110.4f));
        Assert.Equal("0 / 100", VitalsHudText.Pair(0.99f, 100f));
        Assert.Equal("0 / 0", VitalsHudText.Pair(-5f, float.NaN));
        Assert.Equal(0, VitalsHudText.Whole(float.PositiveInfinity));
        Assert.Equal(100, VitalsHudText.Whole(100f));
        Assert.Equal(99, VitalsHudText.Whole(99.9999f));
    }
}
