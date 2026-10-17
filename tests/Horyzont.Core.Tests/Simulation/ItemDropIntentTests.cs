using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Items;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Items.ItemFixture;

namespace Horyzont.Core.Tests.Simulation;

/// <summary><see cref="ItemDropIntent"/> destroys items with the sink Despawn and refuses tools (K6-29W D13).</summary>
public sealed class ItemDropIntentTests
{
    private static ItemFixture Fixture(int players = 1) => new(players, content: SurvivalContentFactory.CreateTest());

    private static void AssertRejected(ItemFixture f, Intent intent, RejectReason reason)
    {
        var hash = StateHasher.Hash(f.State);

        Assert.Equal(reason, f.Sim.Sim.Submit(Player1, intent).Reason);

        Assert.Equal(hash, StateHasher.Hash(f.State));
    }

    [Fact]
    public void Drop_Resource_DestroysWithDespawn()
    {
        var f = Fixture();
        f.Apply(Create(BagSlot(4), Stone, 30));

        var result = f.Sim.Sim.Submit(Player1, new ItemDropIntent(BagSlot(4), 12, Stone, 30));

        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.Equal(new ItemDestroyOp(BagSlot(4), Stone, 12, ItemSink.Despawn), result.Applied!.Operation);
        Assert.Equal(new ItemStack(Stone, 18), f.Slot(BagSlot(4)));
    }

    [Fact]
    public void Drop_SlotChangedSinceTheClientSawIt_ContainerChanged()
    {
        var f = Fixture();
        f.Apply(Create(BagSlot(4), Stone, 30));

        AssertRejected(f, new ItemDropIntent(BagSlot(4), 12, Stone, 31), RejectReason.ContainerChanged);
        AssertRejected(f, new ItemDropIntent(BagSlot(4), 12, Gem, 30), RejectReason.ContainerChanged);
        Assert.Equal(new ItemStack(Stone, 30), f.Slot(BagSlot(4)));
    }

    [Fact]
    public void Drop_Tool_InvalidParameters()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(0), Tool, 1, limit: 1));

        AssertRejected(f, new ItemDropIntent(HotbarSlot(0), 1, Tool, 1), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Drop_MoreThanTheSlotHolds_NotEnoughItems()
    {
        var f = Fixture();
        f.Apply(Create(BagSlot(4), Stone, 30));

        AssertRejected(f, new ItemDropIntent(BagSlot(4), 31, Stone, 30), RejectReason.NotEnoughItems);
        AssertRejected(f, new ItemDropIntent(BagSlot(5), 1, default, 0), RejectReason.NotEnoughItems);
    }

    [Fact]
    public void Drop_BadParameters_Rejected()
    {
        var f = Fixture(players: 2);
        f.Apply(Create(BagSlot(4), Stone, 30));
        f.Apply(Create(new SlotRef(WorldFactory.BagOf(2), 0), Stone, 30));

        AssertRejected(f, new ItemDropIntent(BagSlot(4), 0, Stone, 30), RejectReason.InvalidParameters);
        AssertRejected(f, new ItemDropIntent(BagSlot(WorldFactory.BagSize), 1, Stone, 30), RejectReason.InvalidParameters);
        AssertRejected(f, new ItemDropIntent(new SlotRef(WorldFactory.EquipmentOf(1), 0), 1, Stone, 30), RejectReason.InvalidParameters);
        AssertRejected(f, new ItemDropIntent(new SlotRef(WorldFactory.BagOf(2), 0), 1, Stone, 30), RejectReason.ContainerLocked);
    }

    [Fact]
    public void Drop_WithoutContent_InvalidParameters()
    {
        var f = new ItemFixture();
        f.Apply(Create(BagSlot(4), Stone, 30));

        AssertRejected(f, new ItemDropIntent(BagSlot(4), 1, Stone, 30), RejectReason.InvalidParameters);
        Assert.Equal(new PlayerId(1), Player1);
    }
}
