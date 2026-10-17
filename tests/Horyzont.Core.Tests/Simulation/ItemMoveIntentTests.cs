using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Items;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Items.ItemFixture;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>Validation and resolution of <see cref="ItemMoveIntent"/> (K6-29W, "Walidacja i rozwiązanie intencji przedmiotów").</summary>
public sealed class ItemMoveIntentTests
{
    private static ItemFixture Fixture(int players = 1) => new(players, content: SurvivalContentFactory.CreateTest());

    private static ItemMoveIntent Move(SlotRef from, SlotRef to, int count, ItemTypeId type, int expected) =>
        new(from, ItemMoveTarget.ToSlot(to), (ushort)count, type, (ushort)expected);

    private static ItemMoveIntent MoveAny(SlotRef from, ContainerId to, int count, ItemTypeId type, int expected) =>
        new(from, ItemMoveTarget.AnyIn(to), (ushort)count, type, (ushort)expected);

    private static IntentResult Submit(ItemFixture f, Intent intent) => f.Sim.Sim.Submit(Player1, intent);

    private static void AssertRejected(ItemFixture f, Intent intent, RejectReason reason)
    {
        var hash = StateHasher.Hash(f.State);
        var journaled = f.Sim.Journal.Entries.Count;

        Assert.Equal(reason, Submit(f, intent).Reason);

        Assert.Equal(hash, StateHasher.Hash(f.State));
        Assert.Equal(journaled, f.Sim.Journal.Entries.Count);
    }

    [Fact]
    public void Move_WholeStackToEmptySlot_Transfer()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(1), Gem, 3, limit: 5));

        var result = Submit(f, Move(HotbarSlot(1), BagSlot(0), 3, Gem, 3));

        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.Equal(new ItemTransferOp(HotbarSlot(1), BagSlot(0), Gem, 3, 5), result.Applied!.Operation);
        Assert.True(f.Slot(HotbarSlot(1)).IsEmpty);
        Assert.Equal(new ItemStack(Gem, 3), f.Slot(BagSlot(0)));
        Assert.Equal(Player1, f.Sim.Journal.Entries[^1].Actor);
    }

    [Fact]
    public void Move_PartToEmptySlot_Splits()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(1), Gem, 3, limit: 5));

        var result = Submit(f, Move(HotbarSlot(1), BagSlot(0), 1, Gem, 3));

        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.Equal(new ItemStack(Gem, 2), f.Slot(HotbarSlot(1)));
        Assert.Equal(new ItemStack(Gem, 1), f.Slot(BagSlot(0)));
    }

    [Fact]
    public void Move_ToSameType_MergesUpToTheStackLimit()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(1), Gem, 4, limit: 5));
        f.Apply(Create(BagSlot(0), Gem, 3, limit: 5));

        var result = Submit(f, Move(HotbarSlot(1), BagSlot(0), 4, Gem, 4));

        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.Equal(new ItemTransferOp(HotbarSlot(1), BagSlot(0), Gem, 2, 5), result.Applied!.Operation);
        Assert.Equal(new ItemStack(Gem, 2), f.Slot(HotbarSlot(1)));
        Assert.Equal(new ItemStack(Gem, 5), f.Slot(BagSlot(0)));
    }

    [Fact]
    public void Move_ToFullStackOfSameType_InventoryFull()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(1), Gem, 2, limit: 5));
        f.Apply(Create(BagSlot(0), Gem, 5, limit: 5));

        AssertRejected(f, Move(HotbarSlot(1), BagSlot(0), 2, Gem, 2), RejectReason.InventoryFull);
        Assert.Equal(40, (byte)RejectReason.InventoryFull);
    }

    [Fact]
    public void Move_WholeStackOntoOtherType_Swaps()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(1), Gem, 3, limit: 5));
        f.Apply(Create(BagSlot(0), Stone, 10));

        var result = Submit(f, Move(HotbarSlot(1), BagSlot(0), 3, Gem, 3));

        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.Equal(new ItemSwapOp(HotbarSlot(1), BagSlot(0), Gem, Stone), result.Applied!.Operation);
        Assert.Equal(new ItemStack(Stone, 10), f.Slot(HotbarSlot(1)));
        Assert.Equal(new ItemStack(Gem, 3), f.Slot(BagSlot(0)));
    }

    [Fact]
    public void Move_PartOntoOtherType_InvalidParameters()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(1), Gem, 3, limit: 5));
        f.Apply(Create(BagSlot(0), Stone, 10));

        AssertRejected(f, Move(HotbarSlot(1), BagSlot(0), 2, Gem, 3), RejectReason.InvalidParameters);
    }

    [Fact]
    public void MoveAny_FillsStacksFirst_ThenEmptySlotsAscending()
    {
        var f = Fixture();
        f.Apply(Create(BagSlot(2), Stone, 499));
        f.Apply(Create(BagSlot(5), Stone, 500));
        f.Apply(Create(BagSlot(0), Gem, 1, limit: 5));
        f.Apply(Create(HotbarSlot(3), Stone, 10));

        var result = Submit(f, MoveAny(HotbarSlot(3), Bag, 10, Stone, 10));

        Assert.True(result.IsAccepted, $"{result.Reason}");
        var compound = Assert.IsType<CompoundOp>(result.Applied!.Operation);
        Assert.Equal(
            [new ItemTransferOp(HotbarSlot(3), BagSlot(2), Stone, 1, 500), new ItemTransferOp(HotbarSlot(3), BagSlot(1), Stone, 9, 500)],
            compound.Components);
        Assert.Equal(new ItemStack(Stone, 500), f.Slot(BagSlot(2)));
        Assert.Equal(new ItemStack(Stone, 9), f.Slot(BagSlot(1)));
        Assert.True(f.Slot(HotbarSlot(3)).IsEmpty);
    }

    [Fact]
    public void MoveAny_NothingFits_InventoryFull()
    {
        var f = Fixture();
        f.Apply(FillWithTools(Bag, WorldFactory.BagSize));
        f.Apply(Create(HotbarSlot(3), Stone, 10));

        AssertRejected(f, MoveAny(HotbarSlot(3), Bag, 10, Stone, 10), RejectReason.InventoryFull);
    }

    [Fact]
    public void MoveAny_PartFits_MovesWhatFits()
    {
        var f = Fixture();
        f.Apply(FillWithTools(Bag, WorldFactory.BagSize - 1));
        f.Apply(Create(BagSlot(WorldFactory.BagSize - 1), Stone, 495));
        f.Apply(Create(HotbarSlot(3), Stone, 10));

        var result = Submit(f, MoveAny(HotbarSlot(3), Bag, 10, Stone, 10));

        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.Equal(new ItemStack(Stone, 500), f.Slot(BagSlot(WorldFactory.BagSize - 1)));
        Assert.Equal(new ItemStack(Stone, 5), f.Slot(HotbarSlot(3)));
    }

    [Fact]
    public void MoveAny_IntoTheSourceContainer_InvalidParameters()
    {
        var f = Fixture();
        f.Apply(Create(BagSlot(0), Stone, 10));

        AssertRejected(f, MoveAny(BagSlot(0), Bag, 10, Stone, 10), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Move_SourceDiffersFromExpected_ContainerChanged()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(1), Gem, 3, limit: 5));

        AssertRejected(f, Move(HotbarSlot(1), BagSlot(0), 1, Gem, 4), RejectReason.ContainerChanged);
        AssertRejected(f, Move(HotbarSlot(1), BagSlot(0), 1, Stone, 3), RejectReason.ContainerChanged);
        AssertRejected(f, Move(HotbarSlot(2), BagSlot(0), 1, Gem, 3), RejectReason.ContainerChanged);
        Assert.Equal(42, (byte)RejectReason.ContainerChanged);
    }

    [Fact]
    public void Move_MoreThanExpected_NotEnoughItems()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(1), Gem, 3, limit: 5));

        AssertRejected(f, Move(HotbarSlot(1), BagSlot(0), 4, Gem, 3), RejectReason.NotEnoughItems);
        Assert.Equal(41, (byte)RejectReason.NotEnoughItems);
    }

    [Fact]
    public void Move_ForeignContainer_ContainerLocked()
    {
        var f = Fixture(players: 2);
        f.Apply(Create(HotbarSlot(1), Gem, 3, limit: 5));
        f.Apply(Create(new SlotRef(WorldFactory.BagOf(2), 0), Gem, 3, limit: 5));

        AssertRejected(f, Move(HotbarSlot(1), new SlotRef(WorldFactory.BagOf(2), 0), 1, Gem, 3), RejectReason.ContainerLocked);
        AssertRejected(f, Move(new SlotRef(WorldFactory.BagOf(2), 0), BagSlot(0), 1, Gem, 3), RejectReason.ContainerLocked);
        AssertRejected(f, MoveAny(HotbarSlot(1), WorldFactory.HotbarOf(2), 1, Gem, 3), RejectReason.ContainerLocked);
        AssertRejected(f, Move(HotbarSlot(1), new SlotRef(new ContainerId(999), 0), 1, Gem, 3), RejectReason.ContainerLocked);
        Assert.Equal(43, (byte)RejectReason.ContainerLocked);
    }

    [Fact]
    public void Move_ToOrFromEquipment_InvalidParameters()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(1), Gem, 3, limit: 5));
        var equipment = WorldFactory.EquipmentOf(1);

        AssertRejected(f, Move(HotbarSlot(1), new SlotRef(equipment, 0), 3, Gem, 3), RejectReason.InvalidParameters);
        AssertRejected(f, MoveAny(HotbarSlot(1), equipment, 3, Gem, 3), RejectReason.InvalidParameters);
        AssertRejected(f, Move(new SlotRef(equipment, 0), BagSlot(0), 1, Gem, 1), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Move_BadNumbers_InvalidParameters()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(1), Gem, 3, limit: 5));

        AssertRejected(f, Move(HotbarSlot(1), BagSlot(0), 0, Gem, 3), RejectReason.InvalidParameters);
        AssertRejected(f, Move(HotbarSlot(1), BagSlot(0), 1, Gem, 0), RejectReason.InvalidParameters);
        AssertRejected(f, Move(HotbarSlot(1), BagSlot(0), 1, ItemTypeId.None, 3), RejectReason.InvalidParameters);
        AssertRejected(f, Move(HotbarSlot(1), BagSlot(WorldFactory.BagSize), 1, Gem, 3), RejectReason.InvalidParameters);
        AssertRejected(f, Move(HotbarSlot(WorldFactory.HotbarSize), BagSlot(0), 1, Gem, 3), RejectReason.InvalidParameters);
        AssertRejected(f, Move(HotbarSlot(1), HotbarSlot(1), 1, Gem, 3), RejectReason.InvalidParameters);
        AssertRejected(f, new ItemMoveIntent(HotbarSlot(1), new ItemMoveTarget(Bag, 3, true), 1, Gem, 3), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Move_WithoutContent_InvalidParameters()
    {
        var f = new ItemFixture();
        f.Apply(Create(HotbarSlot(1), Gem, 3, limit: 5));

        AssertRejected(f, Move(HotbarSlot(1), BagSlot(0), 3, Gem, 3), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Move_UnknownPlayerAndOffline_CheckedFirst()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(1), Gem, 3, limit: 5));
        var move = Move(HotbarSlot(1), BagSlot(0), 3, Gem, 3);

        Assert.Equal(RejectReason.UnknownPlayer, f.Sim.Sim.Submit(new PlayerId(9), move).Reason);
        f.Sim.Sim.LeaveAllOnlinePlayers();
        Assert.Equal(RejectReason.PlayerOffline, Submit(f, move).Reason);
    }

    [Fact]
    public void Burst41_RateLimited()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(1), Gem, 3, limit: 5));
        var at = HotbarSlot(1);
        var other = BagSlot(0);

        for (var i = 0; i < 40; i++)
        {
            var result = Submit(f, Move(at, other, 3, Gem, 3));
            Assert.True(result.IsAccepted, $"move {i + 1}: {result.Reason}");
            (at, other) = (other, at);
        }

        AssertRejected(f, Move(at, other, 3, Gem, 3), RejectReason.RateLimited);

        // 20 per second at 60 ticks per second: one item intent every 3 ticks.
        f.Sim.Ticks(2);
        Assert.Equal(RejectReason.RateLimited, Submit(f, Move(at, other, 3, Gem, 3)).Reason);
        f.Sim.Ticks(1);
        Assert.True(Submit(f, Move(at, other, 3, Gem, 3)).IsAccepted);
    }

    [Fact]
    public void Move_ContainerLockedBeforeRateLimited_RateLimitedBeforeContainerChanged()
    {
        var f = Fixture(players: 2);
        f.Apply(Create(HotbarSlot(1), Gem, 3, limit: 5));
        var at = HotbarSlot(1);
        var other = BagSlot(0);
        for (var i = 0; i < 40; i++)
        {
            Assert.True(Submit(f, Move(at, other, 3, Gem, 3)).IsAccepted);
            (at, other) = (other, at);
        }

        Assert.Equal(RejectReason.ContainerLocked, Submit(f, Move(at, new SlotRef(WorldFactory.BagOf(2), 0), 3, Gem, 3)).Reason);
        Assert.Equal(RejectReason.RateLimited, Submit(f, Move(at, other, 3, Gem, 2)).Reason);
    }

    [Fact]
    public void ItemIntents_DoNotUseTheTerrainOrBlockBuckets()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(1), Gem, 3, limit: 5));
        var at = HotbarSlot(1);
        var other = BagSlot(0);
        for (var i = 0; i < 40; i++)
        {
            Assert.True(Submit(f, Move(at, other, 3, Gem, 3)).IsAccepted);
            (at, other) = (other, at);
        }

        Assert.True(f.Sim.Sim.Submit(Player1, f.Sim.Dig(Player1, f.Sim.Ground(2))).IsAccepted);
    }

    /// <summary>A compound that puts a tool into the first <paramref name="slots"/> slots of a container.</summary>
    internal static CompoundOp FillWithTools(ContainerId container, int slots)
    {
        var ops = new Operation[slots];
        for (var i = 0; i < slots; i++)
        {
            ops[i] = Create(new SlotRef(container, (ushort)i), Tool, 1, limit: 1);
        }

        return new CompoundOp(ops);
    }
}
