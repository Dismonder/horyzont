using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Tests.Inventory;

public sealed class ClientInventoryTests
{
    [Fact]
    public void State_ReplacesContainer()
    {
        var fx = new InventoryFixture();
        Assert.True(fx.Inventory.TryGetContainer(InventoryFixture.HotbarId, out var hotbar));
        Assert.Equal(new ItemStack(InventoryFixture.Gem, 3), hotbar.Slots[1]);

        fx.State(InventoryFixture.HotbarId, ContainerKind.Hotbar, 10, 7, (4, InventoryFixture.Stone, 20));

        Assert.True(fx.Inventory.TryGetContainer(InventoryFixture.HotbarId, out var replaced));
        Assert.Equal<uint>(7, replaced.Version);
        Assert.True(replaced.Slots[1].IsEmpty);
        Assert.Equal(new ItemStack(InventoryFixture.Stone, 20), replaced.Slots[4]);
        Assert.Same(replaced, fx.Inventory.Hotbar);
        Assert.Same(fx.Inventory.Bag, fx.Inventory.Bag);
        Assert.Equal(40, fx.Inventory.Bag!.Size);
    }

    [Fact]
    public void Delta_NextVersion_Applies()
    {
        var fx = new InventoryFixture();

        fx.Delta(InventoryFixture.BagId, 2, (3, InventoryFixture.Stone, 12));
        fx.Delta(InventoryFixture.BagId, 3, (3, default, 0), (5, InventoryFixture.Soil, 2));

        var bag = fx.Inventory.Bag!;
        Assert.Equal<uint>(3, bag.Version);
        Assert.True(bag.Slots[3].IsEmpty);
        Assert.Equal(new ItemStack(InventoryFixture.Soil, 2), bag.Slots[5]);
        Assert.Empty(fx.Requested);
    }

    [Fact]
    public void Delta_Gap_RequestsResubscribe_IgnoresDelta()
    {
        var fx = new InventoryFixture();

        fx.Delta(InventoryFixture.BagId, 3, (3, InventoryFixture.Stone, 12));
        fx.Delta(InventoryFixture.BagId, 4, (3, InventoryFixture.Stone, 13));

        Assert.Equal<uint>(1, fx.Inventory.Bag!.Version);
        Assert.True(fx.Inventory.Bag.Slots[3].IsEmpty);
        Assert.Equal([InventoryFixture.BagId], fx.Requested); // asked once while waiting for the state

        fx.State(InventoryFixture.BagId, ContainerKind.PlayerBag, 40, 4, (3, InventoryFixture.Stone, 13));
        fx.Delta(InventoryFixture.BagId, 9, (3, InventoryFixture.Stone, 1));

        Assert.Equal([InventoryFixture.BagId, InventoryFixture.BagId], fx.Requested);
    }

    [Fact]
    public void Delta_Old_Ignored()
    {
        var fx = new InventoryFixture();
        fx.Delta(InventoryFixture.BagId, 2, (3, InventoryFixture.Stone, 12));

        fx.Delta(InventoryFixture.BagId, 2, (3, InventoryFixture.Stone, 99));
        fx.Delta(InventoryFixture.BagId, 1, (3, InventoryFixture.Stone, 98));

        Assert.Equal(new ItemStack(InventoryFixture.Stone, 12), fx.Inventory.Bag!.Slots[3]);
        Assert.Equal<uint>(2, fx.Inventory.Bag.Version);
        Assert.Empty(fx.Requested);
    }

    [Fact]
    public void RejectedState_NotStored()
    {
        var fx = new InventoryFixture(withState: false);

        fx.Inventory.OnMessage(ContainerState.Rejected(new(77), RejectReason.ContainerLocked));

        Assert.False(fx.Inventory.TryGetContainer(new(77), out _));
        Assert.Null(fx.Inventory.Bag);
    }

    [Fact]
    public void Delta_RaisesOwnedCountChanges_StateDoesNot()
    {
        var fx = new InventoryFixture(withState: false);
        var changes = new List<(Horyzont.Core.Ids.ItemTypeId Type, int Delta)>();
        fx.Inventory.OwnedCountChanged += (type, delta) => changes.Add((type, delta));
        fx.State(InventoryFixture.BagId, ContainerKind.PlayerBag, 40, 1, (0, InventoryFixture.Stone, 5));
        Assert.Empty(changes);

        fx.Delta(InventoryFixture.BagId, 2, (0, InventoryFixture.Stone, 12), (1, InventoryFixture.Soil, 3));
        fx.Delta(InventoryFixture.BagId, 3, (1, default, 0));

        Assert.Equal([(InventoryFixture.Stone, 7), (InventoryFixture.Soil, 3), (InventoryFixture.Soil, -3)], changes);
    }

    [Fact]
    public void CountOfAndTypeLookup_SumBagAndHotbar()
    {
        var fx = new InventoryFixture();
        fx.Put(InventoryFixture.Bag(2), InventoryFixture.Stone, 30);
        fx.Put(InventoryFixture.Hotbar(4), InventoryFixture.Stone, 12);

        Assert.Equal(42, fx.Inventory.CountOf(InventoryFixture.Stone));
        Assert.Equal(0, fx.Inventory.CountOf(InventoryFixture.Soil));
        Assert.True(fx.Inventory.TryGetTypeId("test:soil", out var soil));
        Assert.Equal(InventoryFixture.Soil, soil);
        Assert.False(fx.Inventory.TryGetTypeId("test:none", out _));
    }

    [Fact]
    public void ItemLookups_UseTheRegistryAndTheCatalog()
    {
        var fx = new InventoryFixture();

        Assert.Equal("test:stone", fx.Inventory.KeyOf(InventoryFixture.Stone));
        Assert.True(fx.Inventory.TryGetDef(InventoryFixture.Gem, out var def));
        Assert.Equal<ushort>(5, def.MaxStack);
        Assert.Null(fx.Inventory.KeyOf(new(99)));
    }

    [Fact]
    public void HasRoomForResources_FalseOnlyWhenNoSlotAndNoOpenResourceStack()
    {
        var fx = new InventoryFixture();
        Assert.True(fx.Inventory.HasRoomForResources());

        for (var i = 0; i < 40; i++)
        {
            fx.Put(InventoryFixture.Bag(i), InventoryFixture.Stone, 500);
        }

        for (var i = 0; i < 10; i++)
        {
            fx.Put(InventoryFixture.Hotbar(i), InventoryFixture.Stone, 500);
        }

        Assert.False(fx.Inventory.HasRoomForResources());
        fx.Put(InventoryFixture.Bag(7), InventoryFixture.Stone, 499);
        Assert.True(fx.Inventory.HasRoomForResources());
    }
}
