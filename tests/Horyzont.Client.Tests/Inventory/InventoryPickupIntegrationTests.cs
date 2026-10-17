using Horyzont.Client.Inventory;
using Horyzont.Core.Items;

namespace Horyzont.Client.Tests.Inventory;

public sealed class InventoryPickupIntegrationTests
{
    [Fact]
    public void Update_FullInventoryAfterAcceptedDig_WarnsExactlyAtGraceBoundary()
    {
        var fx = FullInventory();
        var feed = new PickupToastFeed();
        Assert.False(fx.Inventory.HasRoomForResources());

        feed.OnDigAccepted(1000, inventoryFull: !fx.Inventory.HasRoomForResources());
        feed.Update(1000 + PickupToastFeed.DigGraceMs - 1);
        Assert.Empty(feed.Active);
        feed.Update(1000 + PickupToastFeed.DigGraceMs);

        Assert.Equal(PickupToastKind.InventoryFull, Assert.Single(feed.Active).Kind);
    }

    [Fact]
    public void Update_PickupFillsLastResourceStack_ShowsPickupWithoutFullWarning()
    {
        var fx = FullInventory();
        fx.Put(InventoryFixture.Bag(0), InventoryFixture.Stone, 499);
        Assert.True(fx.Inventory.HasRoomForResources());
        var feed = new PickupToastFeed();
        fx.Inventory.OwnedCountChanged += (type, delta) => feed.OnOwnedCountChanged(type, delta, 1000);

        fx.Put(InventoryFixture.Bag(0), InventoryFixture.Stone, 500);
        Assert.False(fx.Inventory.HasRoomForResources());
        feed.OnDigAccepted(1000, inventoryFull: !fx.Inventory.HasRoomForResources());
        feed.Update(1000 + PickupToastFeed.DigGraceMs);

        var toast = Assert.Single(feed.Active);
        Assert.Equal(PickupToastKind.Pickup, toast.Kind);
        Assert.Equal(InventoryFixture.Stone, toast.Type);
        Assert.Equal(1, toast.Amount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnOwnedCountChanged_ReplicatedTransferInEitherOrder_PreservesEarlierPickup(bool destinationFirst)
    {
        var fx = new InventoryFixture();
        fx.Put(InventoryFixture.Bag(0), InventoryFixture.Stone, 12);
        var feed = new PickupToastFeed();
        feed.OnOwnedCountChanged(InventoryFixture.Stone, 5, 0);
        fx.Inventory.OwnedCountChanged += (type, delta) => feed.OnOwnedCountChanged(type, delta, 500);

        if (destinationFirst)
        {
            fx.Put(InventoryFixture.Hotbar(3), InventoryFixture.Stone, 12);
            fx.Put(InventoryFixture.Bag(0), default, 0);
        }
        else
        {
            fx.Put(InventoryFixture.Bag(0), default, 0);
            fx.Put(InventoryFixture.Hotbar(3), InventoryFixture.Stone, 12);
        }

        Assert.Equal(12, fx.Inventory.CountOf(InventoryFixture.Stone));
        var toast = Assert.Single(feed.Active);
        Assert.Equal(PickupToastKind.Pickup, toast.Kind);
        Assert.Equal(5, toast.Amount);
    }

    [Fact]
    public void HasRoomForResources_OnlyNonResourceStackHasSpace_ReturnsFalse()
    {
        var fx = FullInventory();

        fx.Put(InventoryFixture.Hotbar(0), InventoryFixture.Gem, 1);

        Assert.False(fx.Inventory.HasRoomForResources());
        fx.Put(InventoryFixture.Hotbar(9), default, 0);
        Assert.True(fx.Inventory.HasRoomForResources());
    }

    private static InventoryFixture FullInventory()
    {
        var fx = new InventoryFixture();
        fx.State(InventoryFixture.BagId, ContainerKind.PlayerBag, 40, 2,
            [.. Enumerable.Range(0, 40).Select(slot => (slot, InventoryFixture.Stone, 500))]);
        fx.State(InventoryFixture.HotbarId, ContainerKind.Hotbar, 10, 2,
            [.. Enumerable.Range(0, 10).Select(slot => (slot, InventoryFixture.Stone, 500))]);
        return fx;
    }
}
