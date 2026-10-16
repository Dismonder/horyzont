using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;

using static Horyzont.Core.Tests.Survival.FoodWorld;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-33, contract of operation 41 (<see cref="FoodSetOp"/>, D4, D12). Every refusal starts from the accepted counterpart.</summary>
public sealed class FoodSetOpsTests
{
    [Fact]
    public void FoodSet_NewPlayer_RecordCreated()
    {
        var f = new FoodWorld();
        Assert.Equal(0, f.State.Progress.Count);

        f.Apply(Set(Player1, Slot(Berries, 100)));

        Assert.Equal(1, f.State.Progress.Count);
        Assert.True(f.State.Progress.TryGet(Player1, out var progress));
        Assert.Null(progress.RespawnBed);
        Assert.Null(progress.Discovered);
        Assert.Equal(Slot(Berries, 100), progress.Food[0]);
        Assert.True(progress.Food[1].IsEmpty);
        Assert.True(progress.Food[2].IsEmpty);
        Assert.False(progress.IsEmpty);
    }

    [Fact]
    public void FoodSet_ReplacesAllThreeSlotsAtOnce()
    {
        var f = new FoodWorld();
        f.Apply(Set(Player1, Slot(Berries, 100), Slot(Jam, 200)));

        // The operation sets all three slots, it does not merge: the second slot goes, the third arrives.
        f.Apply(Set(Player1, Slot(Berries, 100), default, Slot(Bread, 300)));

        Assert.Equal(Slots(Slot(Berries, 100), default, Slot(Bread, 300)), f.FoodOf(Player1));
    }

    [Fact]
    public void FoodSet_KeepsBedAndDiscoveries()
    {
        var f = new FoodWorld();
        f.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell, Player1);
        f.Apply(new ItemDiscoverOp(Player1, Stone));
        Assert.True(f.State.Progress.TryGet(Player1, out var before));

        f.Apply(Set(Player1, Slot(Berries, 100)));

        Assert.True(f.State.Progress.TryGet(Player1, out var after));
        Assert.Equal(before.RespawnBed, after.RespawnBed);
        Assert.NotNull(after.RespawnBed);
        Assert.True(after.DiscoveredItems.Contains(Stone));
        Assert.Equal(Slot(Berries, 100), after.Food[0]);
        Assert.Equal(1, f.State.Progress.Count);
    }

    [Fact]
    public void FoodSet_AllEmptyNothingElse_RemovesRecord()
    {
        var f = new FoodWorld();
        f.Apply(Set(Player1, Slot(Berries, 100)));
        Assert.Equal(1, f.State.Progress.Count);

        f.Apply(Set(Player1));

        Assert.Equal(0, f.State.Progress.Count);
        Assert.False(f.State.Progress.TryGet(Player1, out _));
    }

    [Fact]
    public void FoodSet_AllEmptyWithBed_KeepsRecord()
    {
        var f = new FoodWorld();
        f.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell, Player1);
        f.Apply(Set(Player1, Slot(Berries, 100)));

        f.Apply(Set(Player1));

        Assert.True(f.State.Progress.TryGet(Player1, out var progress));
        Assert.NotNull(progress.RespawnBed);
        Assert.True(progress.Food.IsEmpty);
        Assert.False(progress.IsEmpty);
    }

    [Fact]
    public void FoodSet_AllEmptyWithoutRecord_AppliesAndLeavesNoRecord()
    {
        // An empty set for a player without a record is a legal no-op that is still journaled (K6-35 sets empty slots on death).
        var f = new FoodWorld();

        f.Apply(Set(Player1));

        Assert.Equal(0, f.State.Progress.Count);
    }

    [Fact]
    public void FoodSet_UnknownPlayer_Fails()
    {
        var f = new FoodWorld();
        f.Apply(Set(Player1, Slot(Berries, 100)));

        f.AssertFails(Set(new PlayerId(99), Slot(Berries, 100)));
        f.AssertFails(Set(PlayerId.None, Slot(Berries, 100)));
        Assert.Equal(1, f.State.Progress.Count);
    }

    [Fact]
    public void FoodSet_UnknownItemType_Fails()
    {
        var f = new FoodWorld();

        // The registry of the food world has the ids 1 to 8; 8 is accepted, 9 is not.
        f.Apply(Set(Player1, Slot(Bread, 100)));
        f.AssertFails(Set(Player1, Slot(new ItemTypeId(9), 100)));
        f.AssertFails(Set(Player1, Slot(Berries, 100), default, Slot(new ItemTypeId(9), 100)));
        Assert.Equal(Slots(Slot(Bread, 100)), f.FoodOf(Player1));
    }

    [Fact]
    public void FoodSet_TypeAbove4096_Fails()
    {
        var f = new FoodWorld();
        f.Apply(Set(Player1, Slot(Bread, 100)));

        // 4096 is the highest id the format allows (MaxItemTypes), still not in this registry; above it the applier fails on the range.
        f.AssertFails(Set(Player1, Slot(new ItemTypeId(4096), 100)));
        f.AssertFails(Set(Player1, Slot(new ItemTypeId(4097), 100)));
        f.AssertFails(Set(Player1, Slot(new ItemTypeId(ushort.MaxValue), 100)));
    }

    [Fact]
    public void FoodSet_DuplicateTypes_Fails()
    {
        var f = new FoodWorld();

        // The accepted counterpart: two different foods. The same food twice, in any two slots, is refused.
        f.Apply(Set(Player1, Slot(Berries, 100), Slot(Jam, 200)));
        f.AssertFails(Set(Player1, Slot(Berries, 100), Slot(Berries, 200)));
        f.AssertFails(Set(Player1, Slot(Berries, 100), default, Slot(Berries, 100)));
        f.AssertFails(Set(Player1, default, Slot(Jam, 100), Slot(Jam, 100)));
        Assert.Equal(Slots(Slot(Berries, 100), Slot(Jam, 200)), f.FoodOf(Player1));
    }

    [Fact]
    public void FoodSet_EmptySlotWithTick_Fails()
    {
        var f = new FoodWorld();
        f.Apply(Set(Player1, Slot(Berries, 100)));

        // An empty slot has tick 0 (D12).
        f.AssertFails(new FoodSetOp(Player1, Slots(Slot(Berries, 100), new FoodSlot(ItemTypeId.None, new Tick(5)))));
        f.AssertFails(new FoodSetOp(Player1, Slots(new FoodSlot(ItemTypeId.None, new Tick(1)))));
        Assert.Equal(Slots(Slot(Berries, 100)), f.FoodOf(Player1));
    }

    [Fact]
    public void FoodSet_FilledSlotWithTickZero_Applies()
    {
        // The applier does not compare the tick with the current one (precedent K6-32 D19); 0 is a legal tick of filled food.
        var f = new FoodWorld();

        f.Apply(Set(Player1, Slot(Berries, 0)));

        Assert.Equal(Slots(Slot(Berries, 0)), f.FoodOf(Player1));
        Assert.False(f.FoodOf(Player1).IsEmpty);
    }

    [Fact]
    public void FoodSet_InFailedCompound_RestoresRecord()
    {
        var f = new FoodWorld();
        f.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell, Player1);
        f.Apply(Set(Player1, Slot(Berries, 100)));
        Assert.True(f.State.Progress.TryGet(Player1, out var before));
        var destroy = new ItemDestroyOp(BagSlot(Player1, 0), Stone, 1, ItemSink.Admin);

        // The accepted counterpart: the destroy finds the stone that is there.
        var g = new FoodWorld();
        g.Apply(new ItemCreateOp(BagSlot(Player1, 0), Stone, 1, 500, ItemSource.Admin));
        g.Apply(new CompoundOp([Set(Player1, Slot(Jam, 200)), destroy]));
        Assert.Equal(Slots(Slot(Jam, 200)), g.FoodOf(Player1));

        f.AssertFails(new CompoundOp([Set(Player1, Slot(Jam, 200)), destroy]));

        Assert.True(f.State.Progress.TryGet(Player1, out var after));
        Assert.Equal(before, after);
        Assert.Equal(Slots(Slot(Berries, 100)), after.Food);
        Assert.NotNull(after.RespawnBed);
    }

    [Fact]
    public void FoodSet_InFailedCompound_RestoresMissingRecord()
    {
        var destroy = new ItemDestroyOp(BagSlot(Player1, 0), Stone, 1, ItemSink.Admin);

        // The accepted counterpart: with the stone in the bag the compound creates the record.
        var g = new FoodWorld();
        g.Apply(new ItemCreateOp(BagSlot(Player1, 0), Stone, 1, 500, ItemSource.Admin));
        g.Apply(new CompoundOp([Set(Player1, Slot(Jam, 200)), destroy]));
        Assert.Equal(1, g.State.Progress.Count);

        // Without it the compound is refused and no record is left behind.
        var f = new FoodWorld();
        f.AssertFails(new CompoundOp([Set(Player1, Slot(Jam, 200)), destroy]));

        Assert.Equal(0, f.State.Progress.Count);
    }

    [Fact]
    public void EatCompound_DestroyThenFood_AllOrNothing()
    {
        var f = new FoodWorld();
        f.GiveFood(Player1, Berries, 2);
        var after = Slots(Slot(Berries, 500));

        // The accepted counterpart: one berry leaves the bag and the slot is set, in one operation, with the sink Eat.
        var applied = f.Eat(Player1, 0, Berries, after);
        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(ItemSink.Eat, Assert.IsType<ItemDestroyOp>(compound.Components[0]).Sink);
        Assert.IsType<FoodSetOp>(compound.Components[1]);
        Assert.Equal(1, f.BagStack(Player1, 0).Count);
        Assert.Equal(after, f.FoodOf(Player1));

        // Nothing to destroy: the food is not set.
        var empty = new FoodWorld();
        empty.AssertFails(EatOp(Player1, 0, Berries, after));
        Assert.Equal(0, empty.State.Progress.Count);

        // The food slots are invalid (the same food twice): the item stays in the bag.
        var duplicate = new FoodWorld();
        duplicate.GiveFood(Player1, Berries, 2);
        duplicate.AssertFails(EatOp(Player1, 0, Berries, Slots(Slot(Berries, 500), Slot(Berries, 600))));
        Assert.Equal(2, duplicate.BagStack(Player1, 0).Count);
        Assert.Equal(0, duplicate.State.Progress.Count);

        // The last item: the slot is empty afterwards and the food is set.
        var last = new FoodWorld();
        last.GiveFood(Player1, Jam, 1);
        last.Eat(Player1, 0, Jam, Slots(Slot(Jam, 700)));
        Assert.True(last.BagStack(Player1, 0).IsEmpty);
        Assert.Equal(Slots(Slot(Jam, 700)), last.FoodOf(Player1));
    }

    [Fact]
    public void FoodSet_ReplayOfTheJournal_SameHash()
    {
        var f = new FoodWorld();
        f.GiveFood(Player1, Berries, 2);
        f.Eat(Player1, 0, Berries, Slots(Slot(Berries, 100)));
        f.Apply(Set(Player1));

        var replay = new FoodWorld();
        foreach (var entry in f.Blocks.Sim.Journal.Entries)
        {
            replay.Simulation.Replay(entry);
        }

        Assert.Equal(Horyzont.Core.Hashing.StateHasher.Hash(f.State), Horyzont.Core.Hashing.StateHasher.Hash(replay.State));
    }
}
