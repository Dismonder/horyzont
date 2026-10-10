using Horyzont.Client.Inventory;

namespace Horyzont.Client.Tests.Inventory;

public sealed class PickupToastFeedTests
{
    private static readonly Horyzont.Core.Ids.ItemTypeId _stone = InventoryFixture.Stone;
    private static readonly Horyzont.Core.Ids.ItemTypeId _soil = InventoryFixture.Soil;

    [Fact]
    public void IncreasesWithin1500ms_Aggregate()
    {
        var feed = new PickupToastFeed();

        feed.OnOwnedCountChanged(_stone, 5, 0);
        feed.OnOwnedCountChanged(_stone, 7, 1000);
        feed.OnOwnedCountChanged(_soil, 2, 1100);

        Assert.Equal(2, feed.Active.Count);
        var stone = Assert.Single(feed.Active, t => t.Type == _stone);
        Assert.Equal(PickupToastKind.Pickup, stone.Kind);
        Assert.Equal(12, stone.Amount);
        Assert.Equal(2, Assert.Single(feed.Active, t => t.Type == _soil).Amount);
    }

    [Fact]
    public void IncreaseAfterTheWindow_StartsANewToast_AndToastsExpireAfter3s()
    {
        var feed = new PickupToastFeed();
        feed.OnOwnedCountChanged(_stone, 5, 0);

        feed.OnOwnedCountChanged(_stone, 3, 1600);

        Assert.Equal([5, 3], feed.Active.Select(t => t.Amount));
        feed.Update(2999);
        Assert.Equal(2, feed.Active.Count);
        feed.Update(3000);
        Assert.Equal([3], feed.Active.Select(t => t.Amount));
        feed.Update(4600);
        Assert.Empty(feed.Active);
    }

    [Fact]
    public void MoveBetweenContainers_DoesNotToast()
    {
        var feed = new PickupToastFeed();

        // The bag delta and the hotbar delta of one move arrive in the same poll.
        feed.OnOwnedCountChanged(_stone, -12, 500);
        feed.OnOwnedCountChanged(_stone, 12, 500);
        Assert.Empty(feed.Active);

        // The other order (the destination delta first) retracts the toast when the loss follows in the same poll.
        feed.OnOwnedCountChanged(_stone, 12, 700);
        feed.OnOwnedCountChanged(_stone, -12, 700);
        Assert.Empty(feed.Active);

        feed.OnOwnedCountChanged(_stone, 5, 900); // a real gain still shows
        Assert.Equal(5, Assert.Single(feed.Active).Amount);
    }

    [Fact]
    public void InventoryFullAfterAcceptedDig_Warns()
    {
        var feed = new PickupToastFeed();

        feed.OnDigAccepted(0, inventoryFull: true);
        feed.Update(100);
        Assert.Empty(feed.Active);
        feed.Update(400);

        var warning = Assert.Single(feed.Active);
        Assert.Equal(PickupToastKind.InventoryFull, warning.Kind);
        feed.Update(400 + 3000);
        Assert.Empty(feed.Active);
    }

    [Fact]
    public void Dig_WithPickup_OrWithRoom_DoesNotWarn()
    {
        var feed = new PickupToastFeed();
        feed.OnDigAccepted(0, inventoryFull: true);
        feed.OnOwnedCountChanged(_stone, 1, 50); // the pickup arrived after the reply
        feed.Update(500);
        Assert.DoesNotContain(feed.Active, t => t.Kind == PickupToastKind.InventoryFull);

        feed.OnOwnedCountChanged(_soil, 1, 1000); // the pickup arrived before the reply
        feed.OnDigAccepted(1020, inventoryFull: true);
        feed.Update(1500);
        Assert.DoesNotContain(feed.Active, t => t.Kind == PickupToastKind.InventoryFull);

        feed.OnDigAccepted(2000, inventoryFull: false);
        feed.Update(2500);
        Assert.DoesNotContain(feed.Active, t => t.Kind == PickupToastKind.InventoryFull);
    }

    [Fact]
    public void RepeatedWarnings_ShowOneToast()
    {
        var feed = new PickupToastFeed();

        feed.OnDigAccepted(0, inventoryFull: true);
        feed.Update(400);
        feed.OnDigAccepted(500, inventoryFull: true);
        feed.Update(900);

        Assert.Single(feed.Active);
    }
}
