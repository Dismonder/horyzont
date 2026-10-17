using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.World;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-35, D13: a tombstone is a source only. Taking all takes what fits; the last item leaves together with the container and the entity in one operation; nothing
/// can be put into a tombstone, by a move, by a swap or by the quick stack; the container is private and within reach.
/// </summary>
public sealed class TombstoneRecoveryTests
{
    private static readonly PlayerId _p1 = DeathWorld.Player1;
    private static readonly PlayerId _p2 = DeathWorld.Player2;

    /// <summary>A world with the intents on, in which player 1 died with the kit (hard): six stacks in one tombstone beside the body.</summary>
    private static (DeathWorld World, Container Tombstone) Dead(DeathPenalty penalty = DeathPenalty.Hard, int players = 1)
    {
        var w = new DeathWorld(players, survival: true);
        w.GiveKit(_p1);
        w.Die(_p1, penalty);
        return (w, Assert.Single(w.Tombstones()));
    }

    private static CompoundOp FillWithTools(ContainerId container, int firstSlot, int slots)
    {
        var ops = new Operation[slots];
        for (var i = 0; i < slots; i++)
        {
            ops[i] = new ItemCreateOp(new SlotRef(container, (ushort)(firstSlot + i)), DeathWorld.Tool, 1, 1, ItemSource.Admin);
        }

        return new CompoundOp(ops);
    }

    private static int Stacks(Container container)
    {
        var count = 0;
        foreach (var stack in container.Slots)
        {
            count += stack.IsEmpty ? 0 : 1;
        }

        return count;
    }

    [Fact]
    public void TakeAll_Partial_KeepsRemainder()
    {
        var (w, tombstone) = Dead();
        var totals = w.Totals();
        Assert.Equal(6, Stacks(tombstone));

        // Room for two stacks only: the hotbar and the bag are full of tools except two bag slots.
        w.Apply(FillWithTools(DeathWorld.HotbarOf(_p1), 0, 10));
        w.Apply(FillWithTools(DeathWorld.BagOf(_p1), 2, 38));
        var withTools = w.Totals();

        w.Hazard.Blocks.Accept(_p1, new TakeAllIntent(tombstone.Id));

        Assert.True(w.State.Containers.TryGet(tombstone.Id, out var after));
        Assert.Equal(4, Stacks(after));
        Assert.True(w.State.PersistentEntities.TryGet(new EntityId(after.Host.Id), out _));
        Assert.False(w.Container(DeathWorld.BagOf(_p1)).GetSlot(0).IsEmpty);
        Assert.False(w.Container(DeathWorld.BagOf(_p1)).GetSlot(1).IsEmpty);
        Assert.Equal(withTools, w.Totals());
        Assert.NotEqual(totals, withTools);
    }

    [Fact]
    public void TakeAll_Everything_RemovesContainerAndEntityAtomically()
    {
        var (w, tombstone) = Dead();
        var entity = new EntityId(tombstone.Host.Id);
        var totals = w.Totals();

        var applied = w.Hazard.Blocks.Accept(_p1, new TakeAllIntent(tombstone.Id));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.IsType<ContainerRemoveOp>(compound.Components[^2]);
        Assert.Equal(new PersistentEntityRemoveOp(entity), compound.Components[^1]);
        Assert.False(w.State.Containers.TryGet(tombstone.Id, out _));
        Assert.False(w.State.PersistentEntities.TryGet(entity, out _));
        Assert.Contains(applied.ContainerChanges, change => change.Container == tombstone.Id && change.Removed);
        Assert.Equal(totals, w.Totals());
        Assert.NotEmpty(w.Held(_p1));
    }

    [Fact]
    public void LastMove_RemovesContainerAndEntityAtomically()
    {
        var w = new DeathWorld(survival: true);
        w.Put(DeathWorld.BagOf(_p1), 3, DeathWorld.Gem, 4);
        w.Die(_p1, DeathPenalty.Normal);
        var tombstone = Assert.Single(w.Tombstones());
        var entity = new EntityId(tombstone.Host.Id);
        Assert.Equal(1, tombstone.Size);

        // Part of the stack first: the tombstone stays, with the rest.
        w.Hazard.Blocks.Accept(
            _p1, new ItemMoveIntent(new SlotRef(tombstone.Id, 0), ItemMoveTarget.ToSlot(new SlotRef(DeathWorld.BagOf(_p1), 7)), 3, DeathWorld.Gem, 4));
        Assert.Equal(new ItemStack(DeathWorld.Gem, 1), tombstone.GetSlot(0));
        Assert.True(w.State.PersistentEntities.TryGet(entity, out _));

        // The last one: the move, the removal of the container and the removal of the entity are one operation.
        var applied = w.Hazard.Blocks.Accept(
            _p1, new ItemMoveIntent(new SlotRef(tombstone.Id, 0), ItemMoveTarget.AnyIn(DeathWorld.BagOf(_p1)), 1, DeathWorld.Gem, 1));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.IsType<ContainerRemoveOp>(compound.Components[^2]);
        Assert.Equal(new PersistentEntityRemoveOp(entity), compound.Components[^1]);
        Assert.False(w.State.Containers.TryGet(tombstone.Id, out _));
        Assert.False(w.State.PersistentEntities.TryGet(entity, out _));
        Assert.Empty(w.State.PersistentEntities.OwnedBy(_p1));
        var gems = w.Totals()[DeathWorld.Gem.Value];
        Assert.Equal(4, gems);
    }

    [Fact]
    public void MoveOrSwapIntoTombstone_Refused()
    {
        var (w, tombstone) = Dead(DeathPenalty.Normal);
        var bag = DeathWorld.BagOf(_p1);
        w.Put(bag, 10, DeathWorld.Stone, 5);
        w.Put(bag, 11, DeathWorld.Gem, 3);
        Assert.Equal(DeathWorld.Stone, tombstone.GetSlot(0).Type);
        var source = tombstone.GetSlot(0);
        var blocks = w.Hazard.Blocks;

        // Into a slot with the same type, into a slot with another type (a swap), into any slot of the tombstone.
        blocks.AssertRejected(
            _p1, new ItemMoveIntent(new SlotRef(bag, 10), ItemMoveTarget.ToSlot(new SlotRef(tombstone.Id, 0)), 5, DeathWorld.Stone, 5), RejectReason.InvalidParameters);
        blocks.AssertRejected(
            _p1, new ItemMoveIntent(new SlotRef(bag, 11), ItemMoveTarget.ToSlot(new SlotRef(tombstone.Id, 0)), 3, DeathWorld.Gem, 3), RejectReason.InvalidParameters);
        blocks.AssertRejected(
            _p1, new ItemMoveIntent(new SlotRef(bag, 11), ItemMoveTarget.AnyIn(tombstone.Id), 3, DeathWorld.Gem, 3), RejectReason.InvalidParameters);

        // The swap from the other side puts the item of the bag into the tombstone as well: the stone of the tombstone to the slot with the gems.
        blocks.AssertRejected(
            _p1, new ItemMoveIntent(new SlotRef(tombstone.Id, 0), ItemMoveTarget.ToSlot(new SlotRef(bag, 11)), source.Count, source.Type, source.Count),
            RejectReason.InvalidParameters);

        // The quick stack puts away into chests only.
        var before = tombstone.Slots.ToArray();
        blocks.SubmitAs(_p1, new QuickStackIntent());
        Assert.Equal(before, tombstone.Slots.ToArray());
        Assert.Equal(source, tombstone.GetSlot(0));
    }

    [Fact]
    public void OtherPlayer_CannotOpenOrTake()
    {
        var (w, tombstone) = Dead(DeathPenalty.Normal, players: 2);
        var blocks = w.Hazard.Blocks;

        blocks.AssertRejected(_p2, new TakeAllIntent(tombstone.Id), RejectReason.ContainerLocked);
        blocks.AssertRejected(
            _p2, new ItemMoveIntent(new SlotRef(tombstone.Id, 0), ItemMoveTarget.AnyIn(DeathWorld.BagOf(_p2)), 1, DeathWorld.Stone, 30), RejectReason.ContainerLocked);
        Assert.Equal(DeathWorld.Stone, tombstone.GetSlot(0).Type);
    }

    [Fact]
    public void Owner_TooFarAway_OutOfReach()
    {
        var (w, tombstone) = Dead(DeathPenalty.Normal);
        var blocks = w.Hazard.Blocks;
        blocks.Move(_p1, new System.Numerics.Vector3(40f, 4001f, 3f));

        blocks.AssertRejected(_p1, new TakeAllIntent(tombstone.Id), RejectReason.OutOfReach);
        blocks.AssertRejected(
            _p1, new ItemMoveIntent(new SlotRef(tombstone.Id, 0), ItemMoveTarget.AnyIn(DeathWorld.BagOf(_p1)), 1, DeathWorld.Stone, 30), RejectReason.OutOfReach);
    }
}
