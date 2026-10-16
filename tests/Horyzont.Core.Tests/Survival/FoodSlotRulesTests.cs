using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Progress;
using Horyzont.Core.Survival;

using static Horyzont.Core.Tests.Survival.FoodWorld;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-33, D11: where eaten food goes. Berries last 36000 ticks, jam 72000, broth 54000, bread 108000 (60 ticks per second).</summary>
public sealed class FoodSlotRulesTests
{
    /// <summary>Reject reason 49 of the plan; the enum value arrives in step B2.</summary>
    private const RejectReason FoodAlreadyActive = (RejectReason)49;

    private const int TickRate = FoodContent.TickRate;
    private const ulong BerriesTicks = 36000;

    private static readonly VitalsRules _rules = VitalsRules.Default;
    private static FoodTable Table => FoodContent.Table(new FoodWorld().State);

    private static RejectReason Place(FoodSlots current, ItemTypeId type, ulong now, out FoodSlots next) =>
        FoodSlotRules.TryPlace(current, type, Table, new Tick(now), _rules, TickRate, out next);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryPlace_TickRateNotPositive_Throws(int tickRate)
    {
        Assert.Equal(RejectReason.None, FoodSlotRules.TryPlace(FoodSlots.Empty, Berries, Table, new Tick(1000), _rules, TickRate, out _));

        Assert.Throws<ArgumentOutOfRangeException>(() => FoodSlotRules.TryPlace(FoodSlots.Empty, Berries, Table, new Tick(1000), _rules, tickRate, out _));
    }

    [Fact]
    public void TryPlace_AllEmpty_FirstSlot()
    {
        var reason = Place(FoodSlots.Empty, Berries, 1000, out var next);

        Assert.Equal(RejectReason.None, reason);
        Assert.Equal(Slots(Slot(Berries, 1000)), next);
    }

    [Fact]
    public void TryPlace_SameTypeAbove20Percent_FoodAlreadyActive()
    {
        var current = Slots(Slot(Jam, 0), Slot(Berries, 0));

        // The accepted counterpart: another food goes to the free third slot, at the same moment.
        Assert.Equal(RejectReason.None, Place(current, Bread, 18000, out var withBread));
        Assert.Equal(Slots(Slot(Jam, 0), Slot(Berries, 0), Slot(Bread, 18000)), withBread);

        // Half of the berries are left: the same food is refused, and a refusal leaves nothing meaningful in next.
        Assert.Equal(FoodAlreadyActive, Place(current, Berries, 18000, out _));

        // A fresh meal of the same food is refused at once, and so is one with 20.003 percent left (7201 of 36000 ticks).
        Assert.Equal(FoodAlreadyActive, Place(Slots(Slot(Berries, 1000)), Berries, 1000, out _));
        Assert.Equal(FoodAlreadyActive, Place(Slots(Slot(Berries, 0)), Berries, BerriesTicks - 7201, out _));
    }

    [Fact]
    public void TryPlace_SameTypeAtOrBelow20Percent_ReplacesSameSlot()
    {
        var current = Slots(Slot(Jam, 0), Slot(Berries, 0));

        // 7200 of 36000 ticks left is exactly 20 percent: the spec allows the repeat at 20 percent or less (l. 121).
        var reason = Place(current, Berries, BerriesTicks - 7200, out var next);

        Assert.Equal(RejectReason.None, reason);
        Assert.Equal(Slots(Slot(Jam, 0), Slot(Berries, BerriesTicks - 7200)), next);

        // Nearly expired, and expired: the same slot again, with the new tick.
        Assert.Equal(RejectReason.None, Place(current, Berries, BerriesTicks - 10, out var nearly));
        Assert.Equal(Slot(Berries, BerriesTicks - 10), nearly[1]);
        Assert.Equal(Slot(Jam, 0), nearly[0]);
        Assert.Equal(RejectReason.None, Place(current, Berries, BerriesTicks + 5000, out var expired));
        Assert.Equal(Slot(Berries, BerriesTicks + 5000), expired[1]);
    }

    [Fact]
    public void TryPlace_ThreeActiveOtherFoods_FoodAlreadyActive()
    {
        var current = Slots(Slot(Berries, 0), Slot(Jam, 0), Slot(Broth, 0));

        // The accepted counterpart: with one slot expired the new food takes it.
        Assert.Equal(RejectReason.None, Place(current, Bread, BerriesTicks + 100, out var taken));
        Assert.Equal(Slot(Bread, BerriesTicks + 100), taken[0]);

        // Three different active foods, a fourth food: nothing is thrown out (DO USTALENIA-5).
        Assert.Equal(FoodAlreadyActive, Place(current, Bread, 1000, out _));
    }

    [Fact]
    public void TryPlace_ExpiredSlot_Reused()
    {
        var current = Slots(Slot(Berries, 0), default, default);

        // The slot of the expired berries is free again, and it is the lowest free one.
        var reason = Place(current, Jam, BerriesTicks + 1, out var next);

        Assert.Equal(RejectReason.None, reason);
        Assert.Equal(Slots(Slot(Jam, BerriesTicks + 1)), next);

        // A slot whose type has no food statistics any more counts as expired too.
        var stale = Slots(Slot(Stone, 0), Slot(Jam, 0));
        Assert.Equal(RejectReason.None, Place(stale, Bread, 1000, out var afterStale));
        Assert.Equal(Slots(Slot(Bread, 1000), Slot(Jam, 0)), afterStale);
    }

    [Fact]
    public void TryPlace_OtherExpiredSlots_Cleared()
    {
        // At tick 40000 the berries (36000 ticks) have expired, the jam (72000) and the broth (54000) are active.
        // The accepted counterpart: one expired slot is used by the new food and nothing else changes.
        var oneExpired = Slots(Slot(Jam, 0), Slot(Berries, 0), Slot(Broth, 0));
        Assert.Equal(RejectReason.None, Place(oneExpired, Bread, 40000, out var simple));
        Assert.Equal(Slots(Slot(Jam, 0), Slot(Bread, 40000), Slot(Broth, 0)), simple);

        // At tick 60000 the berries (slot 0) and the broth (slot 2) have expired, the jam of slot 1 is active: the new food takes the lowest free slot
        // and the other expired slot becomes empty.
        var twoExpired = Slots(Slot(Berries, 0), Slot(Jam, 50000), Slot(Broth, 0));
        Assert.Equal(RejectReason.None, Place(twoExpired, Bread, 60000, out var cleared));
        Assert.Equal(Slots(Slot(Bread, 60000), Slot(Jam, 50000)), cleared);
    }

    [Fact]
    public void TryPlace_ActiveSlots_KeepIndexAndTick()
    {
        var current = Slots(default, Slot(Broth, 500), default);

        var reason = Place(current, Berries, 1000, out var next);

        Assert.Equal(RejectReason.None, reason);
        Assert.Equal(Slots(Slot(Berries, 1000), Slot(Broth, 500)), next);

        // The active slots never move: a food placed in the lowest free slot leaves slot 1 where it is.
        Assert.Equal(Slot(Broth, 500), next[1]);
        Assert.True(next[2].IsEmpty);
    }

    [Fact]
    public void TryPlace_TypeWithoutFoodStatistics_IsNotPlaced()
    {
        // Stone is no food: the rule is for food only. The resolver refuses it earlier, with InvalidParameters, but the function must not store it.
        var reason = Place(FoodSlots.Empty, Stone, 1000, out var next);

        Assert.Equal(RejectReason.InvalidParameters, reason);
        Assert.True(next.IsEmpty);
    }
}
