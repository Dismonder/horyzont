using Horyzont.Core.Content;

namespace Horyzont.Core.Items;

/// <summary>The seven slots of the equipment container and what each accepts (K6-34, D13).</summary>
public static class EquipmentLayout
{
    /// <summary>Number of slots.</summary>
    public const int SlotCount = 7;

    /// <summary>The kind of a slot: 0 helmet, 1 suit (torso), 2 legs, 3 backpack, 4 to 6 module.</summary>
    /// <param name="slot">0 to 6.</param>
    /// <returns>The kind.</returns>
    public static EquipmentSlot SlotKind(int slot) => throw new NotImplementedException("K6-34 A2");

    /// <summary>
    /// Whether a slot accepts an item with the equipment slot <paramref name="itemSlot"/>. The backpack slot (3) accepts nothing until a package changes the size of the
    /// backpack (D13, DO USTALENIA-5).
    /// </summary>
    /// <param name="slot">0 to 6.</param>
    /// <param name="itemSlot">The equipment slot of the item.</param>
    /// <returns>True when the kinds match; a slot out of range, the backpack slot and <see cref="Content.EquipmentSlot.None"/> accept nothing.</returns>
    public static bool Accepts(int slot, EquipmentSlot itemSlot) => throw new NotImplementedException("K6-34 A2");
}
