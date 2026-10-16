using Horyzont.Core.Content;
using Horyzont.Core.Items;

namespace Horyzont.Client.Inventory;

/// <summary>
/// What the client sends into the equipment (K6-34 D13, D20): the same rule the server uses (<see cref="EquipmentLayout.Accepts"/>, one item per slot, nothing into the slot of the
/// backpack), checked before a move is sent so that a move the server would refuse is not sent.
/// </summary>
public static class EquipmentDropRules
{
    /// <summary>Whether an item may be put into a slot of the equipment.</summary>
    /// <param name="slot">The slot of the equipment, 0 to 6.</param>
    /// <param name="def">The item.</param>
    /// <param name="count">How many items are put there at once.</param>
    /// <returns>True when the server would take it.</returns>
    public static bool CanPlace(int slot, ItemDef? def, int count) => count == 1 && def is not null && EquipmentLayout.Accepts(slot, def.EquipmentSlot);
}
