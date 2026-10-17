using Horyzont.Core.Content;
using Horyzont.Core.Items;

namespace Horyzont.Core.Tests.Items;

/// <summary>K6-34, D13: the seven equipment slots and what each accepts.</summary>
public sealed class EquipmentLayoutTests
{
    /// <summary>The value of <c>EquipmentSlot.Legs</c> (step A2 adds it to the enum).</summary>
    private const EquipmentSlot Legs = (EquipmentSlot)5;

    [Fact]
    public void SevenSlots_Kinds()
    {
        Assert.Equal(7, EquipmentLayout.SlotCount);

        Assert.Equal(EquipmentSlot.Helmet, EquipmentLayout.SlotKind(0));
        Assert.Equal(EquipmentSlot.Suit, EquipmentLayout.SlotKind(1));
        Assert.Equal(Legs, EquipmentLayout.SlotKind(2));
        Assert.Equal(EquipmentSlot.Backpack, EquipmentLayout.SlotKind(3));
        Assert.Equal(EquipmentSlot.Module, EquipmentLayout.SlotKind(4));
        Assert.Equal(EquipmentSlot.Module, EquipmentLayout.SlotKind(5));
        Assert.Equal(EquipmentSlot.Module, EquipmentLayout.SlotKind(6));

        Assert.Throws<ArgumentOutOfRangeException>(() => EquipmentLayout.SlotKind(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => EquipmentLayout.SlotKind(7));
    }

    [Fact]
    public void Accepts_MatchingOnly()
    {
        // Each slot accepts the equipment slot of its kind and nothing else (the backpack slot is the exception below).
        Assert.True(EquipmentLayout.Accepts(0, EquipmentSlot.Helmet));
        Assert.True(EquipmentLayout.Accepts(1, EquipmentSlot.Suit));
        Assert.True(EquipmentLayout.Accepts(2, Legs));
        foreach (var slot in new[] { 4, 5, 6 })
        {
            Assert.True(EquipmentLayout.Accepts(slot, EquipmentSlot.Module));
        }

        Assert.False(EquipmentLayout.Accepts(0, EquipmentSlot.Module));
        Assert.False(EquipmentLayout.Accepts(1, EquipmentSlot.Helmet));
        Assert.False(EquipmentLayout.Accepts(2, EquipmentSlot.Suit));
        Assert.False(EquipmentLayout.Accepts(4, EquipmentSlot.Helmet));
        Assert.False(EquipmentLayout.Accepts(6, Legs));

        // An item that is not wearable goes nowhere, and a slot out of range takes nothing.
        for (var slot = 0; slot < EquipmentLayout.SlotCount; slot++)
        {
            Assert.False(EquipmentLayout.Accepts(slot, EquipmentSlot.None));
        }

        Assert.False(EquipmentLayout.Accepts(-1, EquipmentSlot.Module));
        Assert.False(EquipmentLayout.Accepts(7, EquipmentSlot.Module));
        Assert.False(EquipmentLayout.Accepts(100, EquipmentSlot.Helmet));
    }

    [Fact]
    public void Backpack_RefusedForNow()
    {
        // The slot of the backpack exists but takes no item until a package changes the size of the backpack (D13, DO USTALENIA-5).
        foreach (var itemSlot in new[] { EquipmentSlot.None, EquipmentSlot.Helmet, EquipmentSlot.Suit, EquipmentSlot.Backpack, EquipmentSlot.Module, Legs })
        {
            Assert.False(EquipmentLayout.Accepts(3, itemSlot), $"{itemSlot}");
        }
    }
}
