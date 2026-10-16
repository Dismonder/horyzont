using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Survival;
using Horyzont.Core.World;

using static Horyzont.Core.Tests.Survival.HazardWorld;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-34, contract of operation 42 (<see cref="SuitRefillOp"/>, D6). Every refusal starts from the accepted counterpart.</summary>
public sealed class SuitRefillOpsTests
{
    private static readonly SuitResources _half = new(50f, 50f, 50f, 50f, 50f);

    [Fact]
    public void Refill_AddsAndClampsToCapacity()
    {
        var f = new HazardWorld();
        f.SetSuit(Player1, _half with { Oxygen = 30f });

        f.Apply(Refill(Player1, SuitResource.Oxygen, 50, 200));
        Assert.Equal(80f, f.SuitOf(Player1)!.Value.Oxygen);

        // 80 + 150 would be 230: the capacity of the operation (200) cuts it.
        f.Apply(Refill(Player1, SuitResource.Oxygen, 150, 200));
        Assert.Equal(200f, f.SuitOf(Player1)!.Value.Oxygen);

        // The other resources did not move, and every resource has its own cell.
        Assert.Equal(_half with { Oxygen = 200f }, f.SuitOf(Player1));
        foreach (var resource in Enum.GetValues<SuitResource>().Where(r => r != SuitResource.Oxygen))
        {
            f.Apply(Refill(Player1, resource, 10, 100));
            Assert.Equal(60f, f.SuitOf(Player1)!.Value[resource]);
        }
    }

    [Fact]
    public void Refill_NeverLowers_WhenAboveCapacity()
    {
        // A module taken off between the validation and the operation leaves a value above the capacity of the operation (D6): the refill must not take it away.
        var f = new HazardWorld();
        f.SetSuit(Player1, _half with { Oxygen = 150f });

        f.Apply(Refill(Player1, SuitResource.Oxygen, 50, 100));

        Assert.Equal(150f, f.SuitOf(Player1)!.Value.Oxygen);

        // The accepted counterpart: below the capacity the same operation adds.
        f.SetSuit(Player1, _half with { Oxygen = 20f });
        f.Apply(Refill(Player1, SuitResource.Oxygen, 50, 100));
        Assert.Equal(70f, f.SuitOf(Player1)!.Value.Oxygen);
    }

    [Fact]
    public void Refill_NullVitalsOrSuit_NoChange()
    {
        // A player without any vitals yet: the operation applies (a fresh suit is full anyway) and leaves no vitals behind.
        var f = new HazardWorld();
        Assert.Null(f.VitalsOf(Player1));

        f.Apply(Refill(Player1, SuitResource.Oxygen, 50, 200));

        Assert.Null(f.VitalsOf(Player1));

        // Vitals with a fresh suit (Suit null): nothing changes either, and the suit stays null.
        var g = new HazardWorld();
        g.SetSuit(Player1, null);
        var before = g.VitalsOf(Player1);

        g.Apply(Refill(Player1, SuitResource.Oxygen, 50, 200));

        Assert.Equal(before, g.VitalsOf(Player1));
        Assert.Null(g.SuitOf(Player1));
    }

    [Fact]
    public void Refill_KeepsHealthStaminaAndRest()
    {
        // The operation writes `Vitals with { Suit = ... }`, never a new PlayerVitals (Pułapki of the plan).
        var f = new HazardWorld();
        Assert.True(f.Simulation.UpdatePlayerVitals(Player1, new PlayerVitals(80f, 55.5f, new Tick(36000), true, _half)));

        f.Apply(Refill(Player1, SuitResource.Shield, 25, 100));

        Assert.Equal(new PlayerVitals(80f, 55.5f, new Tick(36000), true, _half with { Shield = 75f }), f.VitalsOf(Player1));
    }

    [Fact]
    public void Refill_UnknownPlayer_Fails()
    {
        var f = new HazardWorld();
        f.SetSuit(Player1, _half);

        f.Apply(Refill(Player1, SuitResource.Oxygen, 10, 100));
        f.AssertFails(Refill(new PlayerId(99), SuitResource.Oxygen, 10, 100));
        f.AssertFails(Refill(PlayerId.None, SuitResource.Oxygen, 10, 100));
        Assert.Equal(60f, f.SuitOf(Player1)!.Value.Oxygen);
    }

    [Fact]
    public void Refill_ResourceOutOfRange_Fails()
    {
        var f = new HazardWorld();
        f.SetSuit(Player1, _half);

        f.Apply(Refill(Player1, SuitResource.Heating, 10, 100));
        f.Apply(Refill(Player1, SuitResource.Oxygen, 10, 100));
        foreach (var bad in new byte[] { 0, 6, 7, 255 })
        {
            f.AssertFails(Refill(Player1, (SuitResource)bad, 10, 100));
        }
    }

    [Fact]
    public void Refill_AmountOrCapacityOutOfRange_Fails()
    {
        var f = new HazardWorld();
        f.SetSuit(Player1, _half);

        // The limits are accepted: 1 and 1000.
        f.Apply(Refill(Player1, SuitResource.Oxygen, 1, 1));
        f.Apply(Refill(Player1, SuitResource.Oxygen, 1000, 1000));
        Assert.Equal(1000f, f.SuitOf(Player1)!.Value.Oxygen);

        f.AssertFails(Refill(Player1, SuitResource.Oxygen, 0, 100));
        f.AssertFails(Refill(Player1, SuitResource.Oxygen, 1001, 1000));
        f.AssertFails(Refill(Player1, SuitResource.Oxygen, ushort.MaxValue, 1000));
        f.AssertFails(Refill(Player1, SuitResource.Oxygen, 10, 0));
        f.AssertFails(Refill(Player1, SuitResource.Oxygen, 10, 1001));
        f.AssertFails(Refill(Player1, SuitResource.Oxygen, 10, ushort.MaxValue));
    }

    [Fact]
    public void Refill_InFailedCompound_RestoresVitals()
    {
        var f = new HazardWorld();
        f.SetSuit(Player1, _half);
        var before = f.VitalsOf(Player1);
        var destroy = new ItemDestroyOp(BagSlot(Player1, 0), Stone, 1, ItemSink.Admin);

        // The accepted counterpart: with the stone in the bag the compound refills and destroys.
        var g = new HazardWorld();
        g.SetSuit(Player1, _half);
        g.Give(Player1, Stone);
        g.Apply(new CompoundOp([Refill(Player1, SuitResource.Oxygen, 10, 100), destroy]));
        Assert.Equal(60f, g.SuitOf(Player1)!.Value.Oxygen);

        // Without the stone the destroy fails after the refill: the refill is undone and the vitals are as before.
        f.AssertFails(new CompoundOp([Refill(Player1, SuitResource.Oxygen, 10, 100), destroy]));

        Assert.Equal(before, f.VitalsOf(Player1));
    }

    [Fact]
    public void Refill_InFailedCompound_RestoresMissingVitals()
    {
        // The refill of a player without vitals changes nothing, so a failed compound leaves the vitals null (the undo must not invent a record).
        var f = new HazardWorld();
        var destroy = new ItemDestroyOp(BagSlot(Player1, 0), Stone, 1, ItemSink.Admin);

        var g = new HazardWorld();
        g.Give(Player1, Stone);
        g.Apply(new CompoundOp([Refill(Player1, SuitResource.Oxygen, 10, 100), destroy]));
        Assert.Null(g.VitalsOf(Player1));

        f.AssertFails(new CompoundOp([Refill(Player1, SuitResource.Oxygen, 10, 100), destroy]));

        Assert.Null(f.VitalsOf(Player1));

        f.SetSuit(Player1, null);
        var before = f.VitalsOf(Player1);
        f.AssertFails(new CompoundOp([Refill(Player1, SuitResource.Oxygen, 10, 100), destroy]));
        Assert.Equal(before, f.VitalsOf(Player1));
        f.Give(Player1, Stone);
        f.Apply(new CompoundOp([Refill(Player1, SuitResource.Oxygen, 10, 100), destroy]));
        Assert.Equal(before, f.VitalsOf(Player1));
    }

    [Fact]
    public void Refill_TwoRefillsThenFailure_UndoInReverseOrder()
    {
        var f = new HazardWorld();
        f.SetSuit(Player1, _half);
        var before = f.VitalsOf(Player1);
        var destroy = new ItemDestroyOp(BagSlot(Player1, 0), Stone, 1, ItemSink.Admin);
        var g = new HazardWorld();
        g.SetSuit(Player1, _half);
        g.Give(Player1, Stone);
        Operation[] parts = [Refill(Player1, SuitResource.Oxygen, 10, 100), Refill(Player1, SuitResource.Oxygen, 20, 100), Refill(Player1, SuitResource.Heating, 5, 100), destroy];
        g.Apply(new CompoundOp(parts));
        Assert.Equal(_half with { Oxygen = 80f, Heating = 55f }, g.SuitOf(Player1));

        f.AssertFails(new CompoundOp(parts));
        Assert.Equal(before, f.VitalsOf(Player1));
    }

    [Fact]
    public void UpdatePlayerVitals_InvalidSuit_Refused()
    {
        var f = new HazardWorld();
        f.SetSuit(Player1, _half);
        var before = f.VitalsOf(Player1);
        foreach (var bad in new[] { float.NaN, 1001f })
        {
            Assert.False(f.Simulation.UpdatePlayerVitals(Player1, before!.Value with { Suit = _half with { Oxygen = bad } }));
            Assert.Equal(before, f.VitalsOf(Player1));
        }
    }

    [Fact]
    public void Refill_MarksPlayerChanged()
    {
        // An offline player is in the checkpoint only when marked changed (an online one always is): the refill of a player who left must reach the checkpoint (D6).
        var f = new HazardWorld(players: 2);
        f.SetSuit(Player1, _half);
        var state = f.Blocks.Sim.PlayerOf(Player1);
        f.Apply(new PlayerLeaveOp(Player1, state.Frame, state.ParentSeq, state.Position, Quaternion.Identity, 0f));
        f.State.CompleteCheckpoint(f.State.CaptureCheckpoint());
        Assert.DoesNotContain(f.State.CaptureCheckpoint().UpsertedPlayers, p => p.Player == Player1);

        f.Apply(Refill(Player1, SuitResource.Oxygen, 10, 100));

        var record = Assert.Single(f.State.CaptureCheckpoint().UpsertedPlayers, p => p.Player == Player1);
        Assert.False(record.IsOnline);
        Assert.Equal(60f, record.Vitals!.Value.Suit!.Value.Oxygen);
    }

    [Fact]
    public void RefillCompound_DestroyThenRefill_AllOrNothing()
    {
        var f = new HazardWorld();
        f.SetSuit(Player1, _half);
        f.Give(Player1, HeatCell, 2);
        var refill = Refill(Player1, SuitResource.Heating, 40, 100);

        // The accepted counterpart: one cell leaves the bag with the sink SuitRefill and the heating rises, in one operation.
        var applied = f.Apply(new CompoundOp([new ItemDestroyOp(BagSlot(Player1, 0), HeatCell, 1, ItemSink.SuitRefill), refill]));
        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(ItemSink.SuitRefill, Assert.IsType<ItemDestroyOp>(compound.Components[0]).Sink);
        Assert.IsType<SuitRefillOp>(compound.Components[1]);
        Assert.Equal(1, f.Blocks.Container(BagOf(Player1)).GetSlot(0).Count);
        Assert.Equal(90f, f.SuitOf(Player1)!.Value.Heating);

        // An invalid refill (amount 0): the item stays in the bag and the suit does not change.
        var before = f.VitalsOf(Player1);
        f.AssertFails(new CompoundOp([new ItemDestroyOp(BagSlot(Player1, 0), HeatCell, 1, ItemSink.SuitRefill), Refill(Player1, SuitResource.Heating, 0, 100)]));
        Assert.Equal(1, f.Blocks.Container(BagOf(Player1)).GetSlot(0).Count);
        Assert.Equal(before, f.VitalsOf(Player1));

        // Nothing to destroy: the suit is not refilled.
        var empty = new HazardWorld();
        empty.SetSuit(Player1, _half);
        empty.AssertFails(new CompoundOp([new ItemDestroyOp(BagSlot(Player1, 0), HeatCell, 1, ItemSink.SuitRefill), refill]));
        Assert.Equal(_half, empty.SuitOf(Player1));
    }

    [Fact]
    public void Refill_ReplayOfTheJournal_SameHash()
    {
        var f = new HazardWorld();
        f.SetSuit(Player1, _half);
        f.Give(Player1, Canister, 3);
        f.Apply(new CompoundOp([new ItemDestroyOp(BagSlot(Player1, 0), Canister, 1, ItemSink.SuitRefill), Refill(Player1, SuitResource.Oxygen, 50, 100)]));
        f.Apply(Refill(Player1, SuitResource.Heating, 10, 100));

        var replay = new HazardWorld();
        replay.SetSuit(Player1, _half);
        foreach (var entry in f.Blocks.Sim.Journal.Entries)
        {
            replay.Simulation.Replay(entry);
        }

        Assert.Equal(Horyzont.Core.Hashing.StateHasher.Hash(f.State), Horyzont.Core.Hashing.StateHasher.Hash(replay.State));
        Assert.Equal(f.SuitOf(Player1), replay.SuitOf(Player1));
    }
}
