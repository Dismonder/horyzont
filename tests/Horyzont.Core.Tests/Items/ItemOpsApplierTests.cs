using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Items.ItemFixture;

namespace Horyzont.Core.Tests.Items;

public sealed class ItemOpsApplierTests
{
    [Fact]
    public void ContainerCreate_PlayerHostWithExtraHostFields_Fails()
    {
        var f = new ItemFixture();
        var id = f.State.ContainerIds.PeekNext();
        var player = ContainerHost.Player(Player1);

        // The codec writes only the player id, so live state and replay would hash different hosts.
        f.AssertFails(new ContainerCreateOp(id, ContainerKind.Chest, player with { Cell = new CellPos(1, 2, 3) }, Player1, ContainerAccess.Private, 5));
        f.AssertFails(new ContainerCreateOp(id, ContainerKind.Chest, player with { Site = 4 }, Player1, ContainerAccess.Private, 5));
        f.AssertFails(new ContainerCreateOp(id, ContainerKind.Chest, player with { Number = 4 }, Player1, ContainerAccess.Private, 5));
        f.Apply(new ContainerCreateOp(id, ContainerKind.Chest, player, Player1, ContainerAccess.Private, 5));
    }

    [Fact]
    public void Create_EmptySlot_SetsStack_VersionPlusOne()
    {
        var f = new ItemFixture();
        var before = f.Container(Bag).Version;

        var applied = f.Apply(Create(BagSlot(3), Stone, 7, source: ItemSource.Harvest));

        Assert.Equal(new ItemStack(Stone, 7), f.Slot(BagSlot(3)));
        Assert.Equal(before + 1, f.Container(Bag).Version);
        var change = Assert.Single(applied.ContainerChanges);
        Assert.Equal(Bag, change.Container);
        Assert.Equal(before + 1, change.Version);
        Assert.Equal([(ushort)3], change.Slots);
        Assert.Empty(applied.TerrainVersions);
    }

    [Fact]
    public void Create_SameTypeUpToLimit_Merges()
    {
        var f = new ItemFixture();
        f.Apply(Create(BagSlot(0), Gem, 3, limit: 5));

        f.Apply(Create(BagSlot(0), Gem, 2, limit: 5));

        Assert.Equal(new ItemStack(Gem, 5), f.Slot(BagSlot(0)));
    }

    [Fact]
    public void Create_OverStackLimit_Fails()
    {
        var f = new ItemFixture();
        f.Apply(Create(BagSlot(0), Gem, 4, limit: 5));

        f.AssertFails(Create(BagSlot(0), Gem, 2, limit: 5));
        f.AssertFails(Create(BagSlot(1), Gem, 6, limit: 5));
    }

    [Fact]
    public void Create_UnknownType_Fails()
    {
        var f = new ItemFixture();

        f.AssertFails(Create(BagSlot(0), new ItemTypeId(99), 1));
        f.AssertFails(Create(BagSlot(0), ItemTypeId.None, 1));
    }

    [Fact]
    public void Create_BadSlotCountOrContainer_Fails()
    {
        var f = new ItemFixture();
        f.Apply(Create(BagSlot(0), Stone, 1));

        f.AssertFails(Create(BagSlot(0), Soil, 1));
        f.AssertFails(Create(BagSlot(WorldFactory.BagSize), Stone, 1));
        f.AssertFails(Create(new SlotRef(new ContainerId(99), 0), Stone, 1));
        f.AssertFails(Create(BagSlot(1), Stone, 0));
        f.AssertFails(Create(BagSlot(1), Stone, 1, limit: 0));
        f.AssertFails(Create(BagSlot(1), Stone, 1, source: 0));
    }

    [Fact]
    public void Transfer_Partial_SplitsStack()
    {
        var f = new ItemFixture();
        f.Apply(Create(BagSlot(0), Stone, 10));
        var before = f.Container(Bag).Version;

        var applied = f.Apply(Transfer(BagSlot(0), BagSlot(1), Stone, 4));

        Assert.Equal(new ItemStack(Stone, 6), f.Slot(BagSlot(0)));
        Assert.Equal(new ItemStack(Stone, 4), f.Slot(BagSlot(1)));
        var change = Assert.Single(applied.ContainerChanges);
        Assert.Equal(before + 1, change.Version);
        Assert.Equal([(ushort)0, (ushort)1], change.Slots);
    }

    [Fact]
    public void Transfer_Whole_EmptiesSource_AndTwoContainersChangeInIdOrder()
    {
        var f = new ItemFixture();
        f.Apply(Create(HotbarSlot(2), Stone, 10));

        var applied = f.Apply(Transfer(HotbarSlot(2), BagSlot(7), Stone, 10));

        Assert.True(f.Slot(HotbarSlot(2)).IsEmpty);
        Assert.Equal(new ItemStack(Stone, 10), f.Slot(BagSlot(7)));
        Assert.Equal([Bag, Hotbar], applied.ContainerChanges.Select(c => c.Container));
        Assert.Equal([(ushort)7], applied.ContainerChanges[0].Slots);
        Assert.Equal([(ushort)2], applied.ContainerChanges[1].Slots);
    }

    [Fact]
    public void Transfer_SameType_MergesUpToLimit()
    {
        var f = new ItemFixture();
        f.Apply(Create(BagSlot(0), Gem, 3, limit: 5));
        f.Apply(Create(BagSlot(1), Gem, 2, limit: 5));

        f.Apply(Transfer(BagSlot(0), BagSlot(1), Gem, 3, limit: 5));

        Assert.True(f.Slot(BagSlot(0)).IsEmpty);
        Assert.Equal(new ItemStack(Gem, 5), f.Slot(BagSlot(1)));
        f.Apply(Create(BagSlot(2), Gem, 1, limit: 5));
        f.AssertFails(Transfer(BagSlot(2), BagSlot(1), Gem, 1, limit: 5));
    }

    [Fact]
    public void Transfer_ToOtherType_Fails()
    {
        var f = new ItemFixture();
        f.Apply(Create(BagSlot(0), Stone, 5));
        f.Apply(Create(BagSlot(1), Soil, 5));

        f.AssertFails(Transfer(BagSlot(0), BagSlot(1), Stone, 1));
    }

    [Fact]
    public void Transfer_BadSourceOrSlots_Fails()
    {
        var f = new ItemFixture();
        f.Apply(Create(BagSlot(0), Stone, 5));

        f.AssertFails(Transfer(BagSlot(0), BagSlot(1), Stone, 6));
        f.AssertFails(Transfer(BagSlot(0), BagSlot(1), Soil, 1));
        f.AssertFails(Transfer(BagSlot(0), BagSlot(0), Stone, 1));
        f.AssertFails(Transfer(BagSlot(0), BagSlot(1), Stone, 0));
        f.AssertFails(Transfer(BagSlot(0), HotbarSlot(WorldFactory.HotbarSize), Stone, 1));
        f.AssertFails(Transfer(BagSlot(0), new SlotRef(new ContainerId(99), 0), Stone, 1));
    }

    [Fact]
    public void Swap_ExpectedTypes_SwapsContents()
    {
        var f = new ItemFixture();
        f.Apply(Create(BagSlot(0), Stone, 5));
        f.Apply(Create(HotbarSlot(0), Tool, 1, limit: 1));

        f.Apply(new ItemSwapOp(BagSlot(0), HotbarSlot(0), Stone, Tool));
        f.Apply(new ItemSwapOp(BagSlot(0), BagSlot(9), Tool, ItemTypeId.None));

        Assert.True(f.Slot(BagSlot(0)).IsEmpty);
        Assert.Equal(new ItemStack(Tool, 1), f.Slot(BagSlot(9)));
        Assert.Equal(new ItemStack(Stone, 5), f.Slot(HotbarSlot(0)));
    }

    [Fact]
    public void Swap_UnexpectedType_Fails()
    {
        var f = new ItemFixture();
        f.Apply(Create(BagSlot(0), Stone, 5));
        f.Apply(Create(BagSlot(1), Soil, 5));

        f.AssertFails(new ItemSwapOp(BagSlot(0), BagSlot(1), Soil, Stone));
        f.AssertFails(new ItemSwapOp(BagSlot(0), BagSlot(1), Stone, ItemTypeId.None));
        f.AssertFails(new ItemSwapOp(BagSlot(5), BagSlot(6), ItemTypeId.None, ItemTypeId.None));
        f.AssertFails(new ItemSwapOp(BagSlot(0), BagSlot(0), Stone, Stone));
    }

    [Fact]
    public void Destroy_Part_And_Whole()
    {
        var f = new ItemFixture();
        f.Apply(Create(BagSlot(0), Stone, 5));

        f.Apply(Destroy(BagSlot(0), Stone, 2, ItemSink.BuildCost));
        Assert.Equal(new ItemStack(Stone, 3), f.Slot(BagSlot(0)));
        f.Apply(Destroy(BagSlot(0), Stone, 3, ItemSink.Despawn));
        Assert.True(f.Slot(BagSlot(0)).IsEmpty);
    }

    [Fact]
    public void Destroy_NotEnough_Fails()
    {
        var f = new ItemFixture();
        f.Apply(Create(BagSlot(0), Stone, 5));

        f.AssertFails(Destroy(BagSlot(0), Stone, 6));
        f.AssertFails(Destroy(BagSlot(0), Soil, 1));
        f.AssertFails(Destroy(BagSlot(1), Stone, 1));
        f.AssertFails(Destroy(BagSlot(0), Stone, 0));
        f.AssertFails(Destroy(BagSlot(0), Stone, 1, sink: 0));
    }

    [Fact]
    public void ContainerCreate_NewId_AllSlotsChanged_VersionOne()
    {
        var f = new ItemFixture();
        var player = new PlayerId(1);
        var id = f.State.ContainerIds.PeekNext();

        var applied = f.Apply(new ContainerCreateOp(id, ContainerKind.Chest, ContainerHost.Player(player), player, ContainerAccess.Shared, 3));

        var container = f.Container(id);
        Assert.Equal(1u, container.Version);
        Assert.Equal(ContainerKind.Chest, container.Kind);
        Assert.Equal(ContainerAccess.Shared, container.Access);
        Assert.Equal(player, container.Owner);
        Assert.Equal(3, container.Size);
        Assert.Equal(id.Value + 1, f.State.ContainerIds.NextValue);
        var change = Assert.Single(applied.ContainerChanges);
        Assert.Equal([(ushort)0, (ushort)1, (ushort)2], change.Slots);
    }

    [Fact]
    public void ContainerCreate_UsedId_Fails()
    {
        var f = new ItemFixture();
        var player = new PlayerId(1);

        f.AssertFails(new ContainerCreateOp(Bag, ContainerKind.Chest, ContainerHost.Player(player), player, ContainerAccess.Private, 4));
        f.AssertFails(new ContainerCreateOp(ContainerId.None, ContainerKind.Chest, ContainerHost.Player(player), player, ContainerAccess.Private, 4));
    }

    [Fact]
    public void ContainerCreate_SecondBagForPlayer_Fails()
    {
        var f = new ItemFixture();
        var player = new PlayerId(1);
        var id = f.State.ContainerIds.PeekNext();

        f.AssertFails(new ContainerCreateOp(id, ContainerKind.PlayerBag, ContainerHost.Player(player), player, ContainerAccess.Private, 40));
    }

    [Fact]
    public void ContainerCreate_BadSizeKindAccessOrPlayer_Fails()
    {
        var f = new ItemFixture();
        var player = new PlayerId(1);
        var id = f.State.ContainerIds.PeekNext();

        f.AssertFails(new ContainerCreateOp(id, ContainerKind.Chest, ContainerHost.Player(player), player, ContainerAccess.Private, 0));
        f.AssertFails(new ContainerCreateOp(id, ContainerKind.Chest, ContainerHost.Player(player), player, ContainerAccess.Private, 257));
        f.AssertFails(new ContainerCreateOp(id, 0, ContainerHost.Player(player), player, ContainerAccess.Private, 4));
        f.AssertFails(new ContainerCreateOp(id, ContainerKind.Chest, ContainerHost.Player(player), player, 0, 4));
        f.AssertFails(new ContainerCreateOp(id, ContainerKind.Chest, ContainerHost.Player(new PlayerId(9)), player, ContainerAccess.Private, 4));
    }

    [Fact]
    public void ContainerCreate_NonPlayerHost_Fails()
    {
        var f = new ItemFixture();
        var player = new PlayerId(1);
        var id = f.State.ContainerIds.PeekNext();

        f.AssertFails(new ContainerCreateOp(
            id, ContainerKind.Chest, ContainerHost.ConstructBlock(new EntityId(1), new CellPos(0, 0, 0)), player, ContainerAccess.Shared, 8));
        f.AssertFails(new ContainerCreateOp(id, ContainerKind.Tombstone, ContainerHost.PersistentEntity(new EntityId(1)), player, ContainerAccess.Private, 8));
        f.AssertFails(new ContainerCreateOp(id, ContainerKind.Chest, ContainerHost.Poi(new PlanetId(1), 2, 3), PlayerId.None, ContainerAccess.Shared, 8));
    }
}
