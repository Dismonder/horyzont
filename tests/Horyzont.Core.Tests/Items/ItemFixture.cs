using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Items;

/// <summary>A <see cref="SimFixture"/> over <see cref="WorldFactory.CreateWithPlayerContainers"/> with helpers for item operations.</summary>
internal sealed class ItemFixture
{
    public ItemFixture(int players = 1, FakeTerrainService? terrain = null)
    {
        Sim = new SimFixture(terrain: terrain, state: WorldFactory.CreateWithPlayerContainers(players));
    }

    public static ItemTypeId Tool => WorldFactory.Item(SurvivalContentFactory.ToolKey);

    public static ItemTypeId Gem => WorldFactory.Item(SurvivalContentFactory.GemKey);

    public static ItemTypeId Stone => WorldFactory.Item(SurvivalContentFactory.StoneKey);

    public static ItemTypeId Soil => WorldFactory.Item(SurvivalContentFactory.SoilKey);

    public static ContainerId Bag => WorldFactory.BagOf(1);

    public static ContainerId Hotbar => WorldFactory.HotbarOf(1);

    public SimFixture Sim { get; }

    public WorldState State => Sim.State;

    public static SlotRef BagSlot(int slot) => new(Bag, (ushort)slot);

    public static SlotRef HotbarSlot(int slot) => new(Hotbar, (ushort)slot);

    public static ItemCreateOp Create(SlotRef slot, ItemTypeId type, int count, int limit = 500, ItemSource source = ItemSource.Admin) =>
        new(slot, type, (ushort)count, (ushort)limit, source);

    public static ItemDestroyOp Destroy(SlotRef slot, ItemTypeId type, int count, ItemSink sink = ItemSink.Admin) =>
        new(slot, type, (ushort)count, sink);

    public static ItemTransferOp Transfer(SlotRef from, SlotRef to, ItemTypeId type, int count, int limit = 500) =>
        new(from, to, type, (ushort)count, (ushort)limit);

    public AppliedOperation Apply(Operation op) => Sim.Sim.ApplyServerOperation(op);

    /// <summary>The operation is refused: it throws, the hash, LastOpId and journal do not change and the simulation is not faulted.</summary>
    public void AssertFails(Operation op)
    {
        var hash = StateHasher.Hash(State);
        var last = State.LastOpId;
        var journaled = Sim.Journal.Entries.Count;

        Assert.Throws<InvalidOperationException>(() => Sim.Sim.ApplyServerOperation(op));

        Assert.Equal(hash, StateHasher.Hash(State));
        Assert.Equal(last, State.LastOpId);
        Assert.Equal(journaled, Sim.Journal.Entries.Count);
        Assert.False(Sim.Sim.IsFaulted);
    }

    public Container Container(ContainerId id) =>
        State.Containers.TryGet(id, out var container) ? container : throw new InvalidOperationException($"No container {id.Value}.");

    public ItemStack Slot(SlotRef slot) => Container(slot.Container).GetSlot(slot.Slot);
}
