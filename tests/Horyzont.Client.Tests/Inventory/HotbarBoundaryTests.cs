using Horyzont.Client.Inventory;
using Horyzont.Core.Items;

namespace Horyzont.Client.Tests.Inventory;

public sealed class HotbarBoundaryTests
{
    [Theory]
    [InlineData(int.MinValue, 1)]
    [InlineData(int.MaxValue, 6)]
    [InlineData(-30, 9)]
    [InlineData(30, 9)]
    [InlineData(0, 9)]
    public void Step_LargeSignedDelta_WrapsFromLastSlot(int steps, int expected)
    {
        var selection = new HotbarSelection();
        selection.SelectKey(0);

        selection.Step(steps);

        Assert.Equal(expected, selection.Selected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(0)]
    public void SelectKey_AfterWheelMovement_SelectsAbsoluteSlotAndAllowsWrapping(int key)
    {
        var selection = new HotbarSelection();
        selection.Step(-13);

        selection.SelectKey(key);

        var expected = key == 0 ? 9 : key - 1;
        Assert.Equal(expected, selection.Selected);
        selection.Step(1);
        Assert.Equal((expected + 1) % HotbarSelection.SlotCount, selection.Selected);
        selection.Step(-1);
        Assert.Equal(expected, selection.Selected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void SelectSlot_InvalidIndex_PreservesSelection(int slot)
    {
        var selection = new HotbarSelection();
        selection.SelectSlot(4);

        selection.SelectSlot(slot);

        Assert.Equal(4, selection.Selected);
    }

    [Fact]
    public void SelectedStack_ContainerReplacedWithSmallerState_ReturnsEmptyWithoutChangingSelection()
    {
        var fx = new InventoryFixture();
        var selection = new HotbarSelection();
        selection.SelectKey(0);
        fx.Put(InventoryFixture.Hotbar(9), InventoryFixture.Stone, 12);
        Assert.False(selection.SelectedStack(fx.Inventory).IsEmpty);

        fx.State(InventoryFixture.HotbarId, ContainerKind.Hotbar, 2, 3, (0, InventoryFixture.Tool, 1));

        Assert.Equal(9, selection.Selected);
        Assert.True(selection.SelectedStack(fx.Inventory).IsEmpty);
    }
}
