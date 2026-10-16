using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Survival;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Survival.HazardWorld;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// <see cref="SuitRefillIntent"/> (intent 26, K6-34 D7) over a world whose simulation has the hazard content of <see cref="HazardTestContent"/>: the heat cell (40 heating) and the oxygen
/// canister (50 oxygen). The result is one compound, <c>ItemDestroyOp(slot, type, 1, SuitRefill)</c> then <c>SuitRefillOp</c>. Every test that expects a refusal first accepts a valid twin,
/// because an unknown intent is refused until step B4 and the refusal alone would prove nothing.
/// </summary>
public sealed class SuitRefillIntentTests
{
    private static readonly SuitResources _half = new(50f, 50f, 50f, 50f, 50f);

    private static ContainerId HotbarOf(PlayerId player) => WorldFactory.HotbarOf((int)player.Value);

    private static SuitRefillIntent RefillBag(PlayerId player, int slot = 0) => new(BagOf(player), (ushort)slot);

    /// <summary>A world with a half-empty suit and 3 heat cells in bag slot 0.</summary>
    private static HazardWorld Stocked(int players = 1, HazardRules? hazards = null)
    {
        var w = new HazardWorld(players, survival: true, hazards);
        w.SetSuit(Player1, _half);
        w.Give(Player1, HeatCell, 3, bagSlot: 0);
        return w;
    }

    private static ItemCreateOp Create(ContainerId container, int slot, ItemTypeId type, int count) =>
        new(new SlotRef(container, (ushort)slot), type, (ushort)count, 100, ItemSource.Admin);

    [Fact]
    public void Refill_FromHotbar_DestroysOneAndRaises()
    {
        var w = new HazardWorld(survival: true);
        w.SetSuit(Player1, _half);
        w.Apply(Create(HotbarOf(Player1), 2, HeatCell, 3));

        var applied = w.Blocks.Accept(Player1, new SuitRefillIntent(HotbarOf(Player1), 2));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(
            [
                new ItemDestroyOp(new SlotRef(HotbarOf(Player1), 2), HeatCell, 1, ItemSink.SuitRefill),
                new SuitRefillOp(Player1, SuitResource.Heating, 40, 100),
            ],
            compound.Components);
        Assert.Equal(new ItemStack(HeatCell, 2), w.Blocks.Container(HotbarOf(Player1)).GetSlot(2));
        Assert.Equal(_half with { Heating = 90f }, w.SuitOf(Player1));
    }

    [Fact]
    public void Refill_FromBag_Works()
    {
        var w = Stocked();
        w.Give(Player1, Canister, 2, bagSlot: 1);

        var applied = w.Blocks.Accept(Player1, RefillBag(Player1, 1));

        Assert.Equal(
            [new ItemDestroyOp(BagSlot(Player1, 1), Canister, 1, ItemSink.SuitRefill), new SuitRefillOp(Player1, SuitResource.Oxygen, 50, 100)],
            Assert.IsType<CompoundOp>(applied.Operation).Components);
        Assert.Equal(new ItemStack(Canister, 1), w.Blocks.Container(BagOf(Player1)).GetSlot(1));
        Assert.Equal(_half with { Oxygen = 100f }, w.SuitOf(Player1));
    }

    [Fact]
    public void Refill_FromChest_InvalidParameters()
    {
        var w = Stocked();
        var chest = w.Blocks.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        w.Apply(Create(chest, 0, Canister, 2));
        w.Blocks.Accept(Player1, RefillBag(Player1));

        // A chest the player owns and stands in front of: the refill is from the own bag and hotbar only (D7).
        w.Blocks.AssertRejected(Player1, new SuitRefillIntent(chest, 0), RejectReason.InvalidParameters);
        Assert.Equal(new ItemStack(Canister, 2), w.Blocks.Container(chest).GetSlot(0));
    }

    [Fact]
    public void Refill_OtherPlayersBag_InvalidParameters()
    {
        var w = new HazardWorld(2, survival: true);
        w.SetSuit(Player1, _half);
        w.Give(Player2, HeatCell, 3);
        w.Give(Player1, HeatCell, 1);
        w.Blocks.Accept(Player1, RefillBag(Player1));

        w.Blocks.AssertRejected(Player1, RefillBag(Player2), RejectReason.InvalidParameters);
        Assert.Equal(new ItemStack(HeatCell, 3), w.Blocks.Container(BagOf(Player2)).GetSlot(0));
    }

    [Fact]
    public void Refill_FromEquipment_InvalidParameters()
    {
        var w = Stocked();
        w.Blocks.Accept(Player1, RefillBag(Player1));

        w.Blocks.AssertRejected(Player1, new SuitRefillIntent(EquipmentOf(Player1), 0), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Refill_EmptySlot_NotEnoughItems()
    {
        var w = Stocked();
        w.Blocks.Accept(Player1, RefillBag(Player1));

        w.Blocks.AssertRejected(Player1, RefillBag(Player1, 5), RejectReason.NotEnoughItems);
        w.Blocks.AssertRejected(Player1, new SuitRefillIntent(HotbarOf(Player1), 9), RejectReason.NotEnoughItems);
        w.Blocks.AssertRejected(Player1, RefillBag(Player1, 40), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Refill_NotARefillItem_InvalidParameters()
    {
        var w = Stocked();
        w.Give(Player1, Stone, 5, bagSlot: 1);
        w.Blocks.Accept(Player1, RefillBag(Player1));

        w.Blocks.AssertRejected(Player1, RefillBag(Player1, 1), RejectReason.InvalidParameters);
        Assert.Equal(new ItemStack(Stone, 5), w.Blocks.Container(BagOf(Player1)).GetSlot(1));
    }

    [Fact]
    public void Refill_ResourceFull_InvalidParameters()
    {
        var w = Stocked();
        w.Give(Player1, Canister, 2, bagSlot: 1);
        w.Blocks.Accept(Player1, RefillBag(Player1));

        // Oxygen is at 50 of 100 and heating was raised to 90; set heating to the capacity: the heat cell is refused and the canister still goes (DO USTALENIA-1).
        w.SetSuit(Player1, _half with { Heating = 100f });
        w.Blocks.AssertRejected(Player1, RefillBag(Player1), RejectReason.InvalidParameters);
        Assert.Equal(new ItemStack(HeatCell, 2), w.Blocks.Container(BagOf(Player1)).GetSlot(0));
        w.Blocks.Accept(Player1, RefillBag(Player1, 1));
    }

    [Fact]
    public void Refill_FreshSuitIsFull_InvalidParameters()
    {
        var w = Stocked();
        w.Blocks.Accept(Player1, RefillBag(Player1));

        // A player without a suit (the fresh state) holds the capacity in every resource, so nothing can be added.
        w.SetSuit(Player1, null);
        w.Blocks.AssertRejected(Player1, RefillBag(Player1), RejectReason.InvalidParameters);
        Assert.Null(w.SuitOf(Player1));
    }

    [Fact]
    public void Refill_CapacityFromModules()
    {
        var w = new HazardWorld(survival: true);
        w.SetSuit(Player1, _half with { Oxygen = 100f });
        w.Give(Player1, Canister, 3);

        // At 100 of 100 the canister is refused; the tank module (+100 oxygen) lifts the capacity to 200 and the same intent is accepted with that capacity in the operation.
        w.Blocks.AssertRejected(Player1, RefillBag(Player1), RejectReason.InvalidParameters);
        w.Equip(Player1, 4, Tank);
        var applied = w.Blocks.Accept(Player1, RefillBag(Player1));

        Assert.Equal(
            [new ItemDestroyOp(BagSlot(Player1, 0), Canister, 1, ItemSink.SuitRefill), new SuitRefillOp(Player1, SuitResource.Oxygen, 50, 200)],
            Assert.IsType<CompoundOp>(applied.Operation).Components);
        Assert.Equal(150f, w.SuitOf(Player1)!.Value.Oxygen);
    }

    [Fact]
    public void CustomRules_CapacityMatchesStep()
    {
        // The capacity is the one of the HazardRules of the simulation (A1 #3): a base of 150 accepts the refill at 120 where the default 100 refuses it.
        var tuned = new HazardWorld(survival: true, hazards: HazardRules.Default with { SuitBaseCapacity = 150 });
        tuned.SetSuit(Player1, _half with { Heating = 120f });
        tuned.Give(Player1, HeatCell, 2);

        var applied = tuned.Blocks.Accept(Player1, RefillBag(Player1));

        Assert.Equal(
            [new ItemDestroyOp(BagSlot(Player1, 0), HeatCell, 1, ItemSink.SuitRefill), new SuitRefillOp(Player1, SuitResource.Heating, 40, 150)],
            Assert.IsType<CompoundOp>(applied.Operation).Components);
        Assert.Equal(150f, tuned.SuitOf(Player1)!.Value.Heating);

        var standard = new HazardWorld(survival: true);
        standard.SetSuit(Player1, _half with { Heating = 120f });
        standard.Give(Player1, HeatCell, 2);
        standard.Blocks.AssertRejected(Player1, RefillBag(Player1), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Refill_NearlyFull_WholeItemConsumed_ClampedToCapacity()
    {
        var w = new HazardWorld(survival: true);
        w.SetSuit(Player1, _half with { Oxygen = 99f });
        w.Give(Player1, Canister, 2);

        // 99 + 50 would be 149: the capacity cuts it to 100, the canister is gone all the same (the excess is lost; P3-12).
        w.Blocks.Accept(Player1, RefillBag(Player1));

        Assert.Equal(100f, w.SuitOf(Player1)!.Value.Oxygen);
        Assert.Equal(new ItemStack(Canister, 1), w.Blocks.Container(BagOf(Player1)).GetSlot(0));
    }

    [Fact]
    public void Refill_WithoutHazardContent_InvalidParameters()
    {
        var with = Stocked();
        with.Blocks.Accept(Player1, RefillBag(Player1));

        var without = new HazardWorld(survival: true, hazardContent: false);
        without.SetSuit(Player1, _half);
        without.Give(Player1, HeatCell, 3);
        without.Blocks.AssertRejected(Player1, RefillBag(Player1), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Refill_UnknownPlayerAndOffline_CheckedFirst()
    {
        var w = Stocked();
        w.Blocks.Accept(Player1, RefillBag(Player1));

        // A slot outside the container is a parameter error, but the player is checked before the parameters.
        Assert.Equal(RejectReason.UnknownPlayer, w.Blocks.SubmitAs(new PlayerId(9), RefillBag(Player1, 40)).Reason);
        w.Simulation.LeaveAllOnlinePlayers();
        Assert.Equal(RejectReason.PlayerOffline, w.Blocks.Submit(RefillBag(Player1, 40)).Reason);
    }

    [Fact]
    public void Refill_Refusals_DoNotSpendToken()
    {
        var w = Stocked();
        w.Give(Player1, Stone, 5, bagSlot: 1);
        w.Blocks.Accept(Player1, RefillBag(Player1));

        // The token is spent only by an accepted refill (K1-03; review B1 P2-4): an empty slot, a non-refill item and a full resource do not touch the bucket, so 45 refusals
        // (more than the burst of 40) keep their own reasons and a valid refill is still accepted afterwards.
        w.SetSuit(Player1, _half with { Heating = 100f });
        for (var i = 0; i < 45; i++)
        {
            var (intent, reason) = (i % 3) switch
            {
                0 => (RefillBag(Player1, 5), RejectReason.NotEnoughItems),
                1 => (RefillBag(Player1, 1), RejectReason.InvalidParameters),
                _ => (RefillBag(Player1), RejectReason.InvalidParameters),
            };
            Assert.Equal(reason, w.Blocks.Submit(intent).Reason);
        }

        w.SetSuit(Player1, _half);
        Assert.True(w.Blocks.Submit(RefillBag(Player1)).IsAccepted);
    }

    [Fact]
    public void Refill_Bucket_IsItems()
    {
        var w = Stocked();
        w.Apply(Create(BagOf(Player1), 1, Stone, 20));
        var chest = w.Blocks.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);

        // 40 accepted item moves (the bucket class Items: burst 40, 20 per second) empty the bucket.
        for (var i = 0; i < 40; i++)
        {
            var (from, to) = i % 2 == 0 ? (1, 2) : (2, 1);
            var move = new ItemMoveIntent(BagSlot(Player1, from), ItemMoveTarget.ToSlot(BagSlot(Player1, to)), 20, Stone, 20);
            Assert.True(w.Blocks.Submit(move).IsAccepted, $"move {i}");
        }

        // The rate limit comes before the content checks (D7): a refusal that would be NotEnoughItems and a valid refill are both RateLimited, while a parameter error (the chest) comes before the limit.
        w.Blocks.AssertRejected(Player1, RefillBag(Player1, 5), RejectReason.RateLimited);
        w.Blocks.AssertRejected(Player1, RefillBag(Player1), RejectReason.RateLimited);
        w.Blocks.AssertRejected(Player1, new SuitRefillIntent(chest, 0), RejectReason.InvalidParameters);
        w.AdvanceTicks(3);
        Assert.True(w.Blocks.Submit(RefillBag(Player1)).IsAccepted);
    }

    [Fact]
    public void Refill_Journal_OneCompoundEntry()
    {
        var w = Stocked();
        var entries = w.Blocks.Sim.Journal.Entries.Count;

        w.Blocks.Accept(Player1, RefillBag(Player1));

        Assert.Equal(entries + 1, w.Blocks.Sim.Journal.Entries.Count);
        Assert.IsType<CompoundOp>(w.Blocks.Sim.Journal.Entries[^1].Operation);
    }
}
