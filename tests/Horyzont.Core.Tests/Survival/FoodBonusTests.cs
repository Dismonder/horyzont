using Horyzont.Core.Ids;
using Horyzont.Core.Progress;
using Horyzont.Core.Survival;

using static Horyzont.Core.Tests.Survival.FoodWorld;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-33, D16 and D17: the effects of the three slots add up, each scaled by the food curve.</summary>
public sealed class FoodBonusTests
{
    private const int TickRate = FoodContent.TickRate;

    private static readonly VitalsRules _rules = VitalsRules.Default;

    private static double Strength(ulong eaten, ulong now, ulong durationTicks) =>
        Math.Pow(Math.Clamp(((double)eaten + durationTicks - now) / durationTicks, 0.0, 1.0), 0.3);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Compute_TickRateNotPositive_Throws(int tickRate)
    {
        var table = FoodContent.Table(new FoodWorld().State);
        var slots = Slots(Slot(Berries, 0));
        Assert.True(FoodBonus.Compute(slots, table, new Tick(100), TickRate, _rules).Health > 0f);

        Assert.Throws<ArgumentOutOfRangeException>(() => FoodBonus.Compute(slots, table, new Tick(100), tickRate, _rules));
    }

    [Fact]
    public void Compute_ThreeFoods_SumWithCurve()
    {
        var table = FoodContent.Table(new FoodWorld().State);
        var berriesTicks = FoodContent.DurationTicks(FoodContent.Berries);
        var jamTicks = FoodContent.DurationTicks(FoodContent.Jam);
        var brothTicks = FoodContent.DurationTicks(FoodContent.Broth);
        const ulong Now = 18000;
        var slots = Slots(Slot(Berries, 0), Slot(Jam, 0), Slot(Broth, 0));

        // Berries have 50 percent left, jam 75, broth 66.7.
        var sBerries = Strength(0, Now, berriesTicks);
        var sJam = Strength(0, Now, jamTicks);
        var sBroth = Strength(0, Now, brothTicks);
        var bonus = FoodBonus.Compute(slots, table, new Tick(Now), TickRate, _rules);

        Assert.Equal((10 * sBerries) + (25 * sJam) + (15 * sBroth), bonus.Health, 3);
        Assert.Equal((20 * sBerries) + (35 * sJam) + (0 * sBroth), bonus.Stamina, 3);
        Assert.Equal((0.5 * sBerries) + (1.0 * sJam) + (1.5 * sBroth), bonus.HealthRegenPerSecond, 3);
    }

    [Fact]
    public void Compute_FreshFood_FullEffect()
    {
        var table = FoodContent.Table(new FoodWorld().State);

        var bonus = FoodBonus.Compute(Slots(Slot(Jam, 500)), table, new Tick(500), TickRate, _rules);

        Assert.Equal(new FoodBonus(25f, 35f, 1.0f), bonus);
    }

    [Fact]
    public void Compute_ExpiredOrUnknownType_Ignored()
    {
        var table = FoodContent.Table(new FoodWorld().State);
        var jamTicks = FoodContent.DurationTicks(FoodContent.Jam);
        const ulong Now = 40000;

        // The accepted counterpart: berries (36000 ticks) eaten at 10000 still last at 40000 and add something.
        var active = FoodBonus.Compute(Slots(Slot(Berries, 10000)), table, new Tick(Now), TickRate, _rules);
        Assert.True(active.Health > 0f);

        // Expired berries (eaten at 0, 36000 ticks), a type that is not food (stone) and a type outside the table add nothing; empty slots too. The jam counts.
        var slots = Slots(Slot(Berries, 0), Slot(Stone, 5000), Slot(Jam, 0));
        var bonus = FoodBonus.Compute(slots, table, new Tick(Now), TickRate, _rules);
        var sJam = Strength(0, Now, jamTicks);
        Assert.Equal(25 * sJam, bonus.Health, 3);
        Assert.Equal(35 * sJam, bonus.Stamina, 3);
        Assert.Equal(1.0 * sJam, bonus.HealthRegenPerSecond, 3);

        var unknown = FoodBonus.Compute(Slots(Slot(new ItemTypeId(4000), 0)), table, new Tick(Now), TickRate, _rules);
        Assert.Equal(default, unknown);
        Assert.Equal(default, FoodBonus.Compute(FoodSlots.Empty, table, new Tick(Now), TickRate, _rules));
    }

    [Fact]
    public void Compute_UsesTheTickRate()
    {
        var table = FoodContent.Table(new FoodWorld().State);
        var slots = Slots(Slot(Berries, 0));

        // 600 seconds are 36000 ticks at 60 ticks per second but 6000 ticks at 10: at tick 6000 the food has expired at the second rate only.
        Assert.True(FoodBonus.Compute(slots, table, new Tick(6000), 60, _rules).Health > 0f);
        Assert.Equal(default, FoodBonus.Compute(slots, table, new Tick(6000), 10, _rules));
    }
}
