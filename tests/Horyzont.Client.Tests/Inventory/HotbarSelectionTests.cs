using Horyzont.Client.Inventory;
using Horyzont.Core.Items;

namespace Horyzont.Client.Tests.Inventory;

public sealed class HotbarSelectionTests
{
    [Fact]
    public void Wheel_WrapsAround()
    {
        var selection = new HotbarSelection();
        Assert.Equal(0, selection.Selected);

        selection.Step(-1);
        Assert.Equal(9, selection.Selected);
        selection.Step(1);
        Assert.Equal(0, selection.Selected);
        selection.Step(11);
        Assert.Equal(1, selection.Selected);
        selection.Step(-23);
        Assert.Equal(8, selection.Selected);
    }

    [Fact]
    public void Keys1To0_SelectSlots()
    {
        var selection = new HotbarSelection();

        for (var key = 1; key <= 9; key++)
        {
            selection.SelectKey(key);
            Assert.Equal(key - 1, selection.Selected);
        }

        selection.SelectKey(0);
        Assert.Equal(9, selection.Selected);
        selection.SelectKey(10);
        selection.SelectKey(-1);
        Assert.Equal(9, selection.Selected);
    }

    [Fact]
    public void SelectedItem_ChangesWithDelta()
    {
        var fx = new InventoryFixture();
        var selection = new HotbarSelection();

        Assert.Equal(new ItemStack(InventoryFixture.Tool, 1), selection.SelectedStack(fx.Inventory));
        selection.SelectKey(2);
        Assert.Equal(new ItemStack(InventoryFixture.Gem, 3), selection.SelectedStack(fx.Inventory));

        fx.Delta(InventoryFixture.HotbarId, 2, (1, InventoryFixture.Stone, 40));

        Assert.Equal(new ItemStack(InventoryFixture.Stone, 40), selection.SelectedStack(fx.Inventory));
        selection.SelectKey(5);
        Assert.True(selection.SelectedStack(fx.Inventory).IsEmpty);
    }

    [Fact]
    public void SelectedStack_WithoutHotbar_IsEmpty()
    {
        var fx = new InventoryFixture(withState: false);

        Assert.True(new HotbarSelection().SelectedStack(fx.Inventory).IsEmpty);
    }
}
