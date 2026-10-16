using Horyzont.Client.Inventory;
using Horyzont.Client.Props;
using Horyzont.Client.Tests.Inventory;

namespace Horyzont.Client.Tests.Props;

public sealed class HotbarHeldToolTests
{
    [Fact]
    public void ToolSlot_TierOfTheItem()
    {
        var fixture = new InventoryFixture();
        var selection = new HotbarSelection();
        var held = new HotbarHeldTool(fixture.Inventory, selection);

        Assert.Equal(0, held.HotbarSlot);
        Assert.Equal(1, held.ToolTier);
    }

    [Fact]
    public void ResourceOrComponentSlot_IsTheHand()
    {
        var fixture = new InventoryFixture();
        fixture.Put(InventoryFixture.Hotbar(2), InventoryFixture.Stone, 5);
        var selection = new HotbarSelection();
        var held = new HotbarHeldTool(fixture.Inventory, selection);

        selection.SelectKey(2); // the gem, a component
        Assert.Equal(1, held.HotbarSlot);
        Assert.Equal(0, held.ToolTier);

        selection.SelectKey(3); // the stone, a resource
        Assert.Equal(2, held.HotbarSlot);
        Assert.Equal(0, held.ToolTier);
    }

    [Fact]
    public void EmptySlot_IsTheHand()
    {
        var fixture = new InventoryFixture();
        var selection = new HotbarSelection();
        var held = new HotbarHeldTool(fixture.Inventory, selection);

        selection.SelectKey(0);

        Assert.Equal(9, held.HotbarSlot);
        Assert.Equal(0, held.ToolTier);
    }

    [Fact]
    public void ServerChangeOfTheSlot_CountsAtOnce()
    {
        var fixture = new InventoryFixture();
        var selection = new HotbarSelection();
        var held = new HotbarHeldTool(fixture.Inventory, selection);
        Assert.Equal(1, held.ToolTier);

        fixture.Put(InventoryFixture.Hotbar(0), InventoryFixture.Gem, 1);

        Assert.Equal(0, held.ToolTier);
    }

    [Fact]
    public void BeforeTheHotbarArrived_IsTheHand()
    {
        var fixture = new InventoryFixture(withState: false);

        Assert.Equal(0, new HotbarHeldTool(fixture.Inventory, new HotbarSelection()).ToolTier);
    }
}
