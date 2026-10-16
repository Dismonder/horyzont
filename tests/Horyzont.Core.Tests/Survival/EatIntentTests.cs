using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Survival.FoodWorld;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// <see cref="EatIntent"/> (intent 25, K6-33 D10, D11) over a world whose simulation has the survival content of <see cref="FoodContent"/>: berries (600 s),
/// jam, broth and bread. The result is one compound, <c>ItemDestroyOp(slot, type, 1, Eat)</c> then <c>FoodSetOp</c> (D10). Every test that expects a
/// refusal first accepts a valid twin, because an unknown intent is refused today and the refusal alone would prove nothing. The reject reason 49 is a
/// number until step B2.
/// </summary>
public sealed class EatIntentTests
{
    private const RejectReason FoodAlreadyActive = (RejectReason)49;

    /// <summary>Ticks of berries (600 s at 60 ticks per second).</summary>
    private static readonly ulong _berryTicks = FoodContent.DurationTicks(FoodContent.Berries);

    private static ContainerId HotbarOf(PlayerId player) => WorldFactory.HotbarOf((int)player.Value);

    private static EatIntent EatBag(PlayerId player, int slot = 0) => new(BagOf(player), (ushort)slot);

    private static FoodWorld Stocked(int players = 1, bool survival = true)
    {
        var w = new FoodWorld(players, survival);
        w.GiveFood(Player1, Berries, 3, bagSlot: 0);
        return w;
    }

    private static ItemCreateOp Create(ContainerId container, int slot, ItemTypeId type, int count) =>
        new(new SlotRef(container, (ushort)slot), type, (ushort)count, 20, ItemSource.Admin);

    [Fact]
    public void Eat_FromHotbar_DestroysOneAndSetsFood()
    {
        var w = new FoodWorld(survival: true);
        w.Apply(Create(HotbarOf(Player1), 2, Berries, 3));
        var now = w.Now;

        var applied = w.Blocks.Accept(Player1, new EatIntent(HotbarOf(Player1), 2));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(
            [
                new ItemDestroyOp(new SlotRef(HotbarOf(Player1), 2), Berries, 1, ItemSink.Eat),
                new FoodSetOp(Player1, Slots(Slot(Berries, now.Value))),
            ],
            compound.Components);
        Assert.Equal(new ItemStack(Berries, 2), w.Blocks.Container(HotbarOf(Player1)).GetSlot(2));
        Assert.Equal(Slots(Slot(Berries, now.Value)), w.FoodOf(Player1));
    }

    [Fact]
    public void Eat_FromBag_Works()
    {
        var w = Stocked();
        w.AdvanceTicks(7);
        var now = w.Now;

        var applied = w.Blocks.Accept(Player1, EatBag(Player1));

        Assert.Equal(
            [new ItemDestroyOp(new SlotRef(BagOf(Player1), 0), Berries, 1, ItemSink.Eat), new FoodSetOp(Player1, Slots(Slot(Berries, now.Value)))],
            Assert.IsType<CompoundOp>(applied.Operation).Components);
        Assert.Equal(new ItemStack(Berries, 2), w.BagStack(Player1, 0));
        Assert.Equal(Slots(Slot(Berries, now.Value)), w.FoodOf(Player1));
    }

    [Fact]
    public void Eat_LastItemOfTheStack_EmptiesTheSlot()
    {
        var w = new FoodWorld(survival: true);
        w.GiveFood(Player1, Berries, 1);

        w.Blocks.Accept(Player1, EatBag(Player1));

        Assert.True(w.BagStack(Player1, 0).IsEmpty);
        Assert.Equal(Berries, w.FoodOf(Player1).Slot0.Type);
    }

    [Fact]
    public void Eat_FromChest_InvalidParameters()
    {
        var w = Stocked();
        var chest = w.Blocks.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        w.Apply(Create(chest, 0, Jam, 2));
        w.Blocks.Accept(Player1, EatBag(Player1));

        // The same kind of intent on a chest the player owns and stands in front of: eating is from the own bag and hotbar only (D10, DO USTALENIA-6).
        w.Blocks.AssertRejected(Player1, new EatIntent(chest, 0), RejectReason.InvalidParameters);
        Assert.Equal(new ItemStack(Jam, 2), w.Blocks.Container(chest).GetSlot(0));
    }

    [Fact]
    public void Eat_OtherPlayersBag_InvalidParameters()
    {
        var w = new FoodWorld(2, survival: true);
        w.GiveFood(Player2, Berries, 3);
        w.Blocks.Accept(Player2, EatBag(Player2));

        w.Blocks.AssertRejected(Player1, EatBag(Player2), RejectReason.InvalidParameters);
        Assert.Equal(new ItemStack(Berries, 2), w.BagStack(Player2, 0));
        Assert.True(w.FoodOf(Player1).IsEmpty);
    }

    [Fact]
    public void Eat_EquipmentContainer_InvalidParameters()
    {
        var w = Stocked();
        w.Blocks.Accept(Player1, EatBag(Player1));

        w.Blocks.AssertRejected(Player1, new EatIntent(WorldFactory.EquipmentOf(1), 0), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Eat_EmptySlot_NotEnoughItems()
    {
        var w = Stocked();
        w.Blocks.Accept(Player1, EatBag(Player1));

        w.Blocks.AssertRejected(Player1, EatBag(Player1, 5), RejectReason.NotEnoughItems);
        w.Blocks.AssertRejected(Player1, new EatIntent(HotbarOf(Player1), 9), RejectReason.NotEnoughItems);
    }

    [Fact]
    public void Eat_SlotOutsideTheContainer_InvalidParameters()
    {
        var w = Stocked();
        w.Blocks.Accept(Player1, EatBag(Player1));

        // The last slots are valid (empty); the first slots outside the bag (40 slots) and the hotbar (10 slots) are not.
        w.Blocks.AssertRejected(Player1, EatBag(Player1, 39), RejectReason.NotEnoughItems);
        w.Blocks.AssertRejected(Player1, EatBag(Player1, 40), RejectReason.InvalidParameters);
        w.Blocks.AssertRejected(Player1, new EatIntent(HotbarOf(Player1), 10), RejectReason.InvalidParameters);
        w.Blocks.AssertRejected(Player1, EatBag(Player1, ushort.MaxValue), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Eat_NonFood_InvalidParameters()
    {
        var w = Stocked();
        w.Apply(Create(BagOf(Player1), 1, Stone, 5));
        w.Blocks.Accept(Player1, EatBag(Player1));

        w.Blocks.AssertRejected(Player1, EatBag(Player1, 1), RejectReason.InvalidParameters);
        Assert.Equal(new ItemStack(Stone, 5), w.BagStack(Player1, 1));
    }

    [Fact]
    public void Eat_SameFoodActive_FoodAlreadyActive()
    {
        var w = Stocked();
        var start = w.Now.Value;
        w.Blocks.Accept(Player1, EatBag(Player1));
        w.AdvanceTicks(10);

        w.Blocks.AssertRejected(Player1, EatBag(Player1), FoodAlreadyActive);

        Assert.Equal(new ItemStack(Berries, 2), w.BagStack(Player1, 0));
        Assert.Equal(Slots(Slot(Berries, start)), w.FoodOf(Player1));
    }

    [Fact]
    public void Eat_SameFoodAtOrBelow20Percent_ReplacesSlot()
    {
        var w = Stocked();
        w.Blocks.Accept(Player1, EatBag(Player1));

        // 20.003 percent remain one tick before the boundary (the strict limit of D11), exactly 20 percent at 80 percent of the duration.
        var boundary = (ulong)(_berryTicks * 0.8);
        w.AdvanceTicks((int)boundary - 1);
        w.Blocks.AssertRejected(Player1, EatBag(Player1), FoodAlreadyActive);
        w.AdvanceTicks(1);
        var now = w.Now;

        w.Blocks.Accept(Player1, EatBag(Player1));

        Assert.Equal(Slots(Slot(Berries, now.Value)), w.FoodOf(Player1));
        Assert.Equal(new ItemStack(Berries, 1), w.BagStack(Player1, 0));
    }

    [Fact]
    public void Eat_OtherFoodWhileOneIsActive_UsesTheNextSlotAndKeepsTheFirst()
    {
        var w = Stocked();
        w.GiveFood(Player1, Jam, 2, bagSlot: 1);
        var start = w.Now.Value;
        w.Blocks.Accept(Player1, EatBag(Player1));
        w.AdvanceTicks(100);
        var now = w.Now;

        w.Blocks.Accept(Player1, EatBag(Player1, 1));

        Assert.Equal(Slots(Slot(Berries, start), Slot(Jam, now.Value)), w.FoodOf(Player1));
    }

    [Fact]
    public void Eat_ExpiredSlots_FirstIsReusedAndTheOthersAreCleared()
    {
        var w = new FoodWorld(survival: true);
        w.GiveFood(Player1, Berries, 1, bagSlot: 0);
        w.GiveFood(Player1, Broth, 1, bagSlot: 1);
        w.GiveFood(Player1, Jam, 1, bagSlot: 2);
        w.GiveFood(Player1, Bread, 1, bagSlot: 3);
        var start = w.Now.Value;
        w.Blocks.Accept(Player1, EatBag(Player1, 0));
        w.Blocks.Accept(Player1, EatBag(Player1, 1));
        w.Blocks.Accept(Player1, EatBag(Player1, 2));
        Assert.Equal(Slots(Slot(Berries, start), Slot(Broth, start), Slot(Jam, start)), w.FoodOf(Player1));

        // The berries (600 s) and the broth (900 s) have run out, the jam (1200 s) has not: the bread takes the first free slot (the dead berries) and
        // the dead broth is cleared too, while the jam keeps its slot and its tick (D11, 2 and 4).
        w.AdvanceTicks((int)FoodContent.DurationTicks(FoodContent.Broth));
        var now = w.Now;
        w.Blocks.Accept(Player1, EatBag(Player1, 3));

        Assert.Equal(Slots(Slot(Bread, now.Value), default, Slot(Jam, start)), w.FoodOf(Player1));
    }

    [Fact]
    public void Eat_ThreeActiveFoods_FoodAlreadyActive()
    {
        var w = new FoodWorld(survival: true);
        w.GiveFood(Player1, Berries, 1, bagSlot: 0);
        w.GiveFood(Player1, Jam, 1, bagSlot: 1);
        w.GiveFood(Player1, Broth, 1, bagSlot: 2);
        w.GiveFood(Player1, Bread, 1, bagSlot: 3);
        var start = w.Now.Value;
        w.Blocks.Accept(Player1, EatBag(Player1, 0));
        w.Blocks.Accept(Player1, EatBag(Player1, 1));
        w.Blocks.Accept(Player1, EatBag(Player1, 2));

        // A fourth different food is refused with the same reason (DO USTALENIA-5): the player chooses what to do without losing a meal.
        w.Blocks.AssertRejected(Player1, EatBag(Player1, 3), FoodAlreadyActive);
        Assert.Equal(new ItemStack(Bread, 1), w.BagStack(Player1, 3));
        Assert.Equal(Slots(Slot(Berries, start), Slot(Jam, start), Slot(Broth, start)), w.FoodOf(Player1));
    }

    [Fact]
    public void Eat_WithoutSurvivalContent_InvalidParameters()
    {
        var with = Stocked();
        with.Blocks.Accept(Player1, EatBag(Player1));

        var without = Stocked(survival: false);
        without.Blocks.AssertRejected(Player1, EatBag(Player1), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Eat_UnknownPlayerAndOffline_CheckedFirst()
    {
        var w = Stocked();
        w.Blocks.Accept(Player1, EatBag(Player1));

        // A slot outside the container is a parameter error, but the player is checked before the parameters.
        Assert.Equal(RejectReason.UnknownPlayer, w.Blocks.SubmitAs(new PlayerId(9), EatBag(Player1, 40)).Reason);
        w.Simulation.LeaveAllOnlinePlayers();
        Assert.Equal(RejectReason.PlayerOffline, w.Blocks.Submit(EatBag(Player1, 40)).Reason);
    }

    [Fact]
    public void Eat_Bucket_IsItems()
    {
        var w = Stocked();

        // 40 refused intents use the burst of the Items bucket (20 per second); the rate limit comes before the content checks (D10), so the 41st
        // refusal is RateLimited and not NotEnoughItems, and a valid eating is refused for the same reason until the bucket refills (3 ticks).
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(RejectReason.NotEnoughItems, w.Blocks.Submit(EatBag(Player1, 5)).Reason);
        }

        w.Blocks.AssertRejected(Player1, EatBag(Player1, 5), RejectReason.RateLimited);
        w.Blocks.AssertRejected(Player1, EatBag(Player1), RejectReason.RateLimited);
        w.AdvanceTicks(3);
        Assert.True(w.Blocks.Submit(EatBag(Player1)).IsAccepted);
    }

    [Fact]
    public void Eat_Journal_OneCompoundEntry()
    {
        var w = Stocked();
        var entries = w.Blocks.Sim.Journal.Entries.Count;

        w.Blocks.Accept(Player1, EatBag(Player1));

        Assert.Equal(entries + 1, w.Blocks.Sim.Journal.Entries.Count);
        Assert.IsType<CompoundOp>(w.Blocks.Sim.Journal.Entries[^1].Operation);
    }
}
