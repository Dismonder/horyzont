using Horyzont.Core.Items;

namespace Horyzont.Client.Inventory;

/// <summary>The selected hotbar slot of the local player: client state, not world state (decision D18).</summary>
public sealed class HotbarSelection
{
    /// <summary>The number of hotbar slots a player selects from.</summary>
    public const int SlotCount = 10;

    /// <summary>Gets the selected slot, 0 to <see cref="SlotCount"/> - 1.</summary>
    public int Selected { get; private set; }

    /// <summary>Moves the selection, wrapping around the ends.</summary>
    /// <param name="steps">Signed number of slots (wheel notches).</param>
    public void Step(int steps) => Selected = (((Selected + (steps % SlotCount)) % SlotCount) + SlotCount) % SlotCount;

    /// <summary>Selects a slot.</summary>
    /// <param name="slot">Slot index; a value outside the hotbar is ignored.</param>
    public void SelectSlot(int slot)
    {
        if ((uint)slot < SlotCount)
        {
            Selected = slot;
        }
    }

    /// <summary>Selects the slot of a number key: 1 to 9 select the slots 0 to 8, 0 selects slot 9.</summary>
    /// <param name="key">The digit; other values are ignored.</param>
    public void SelectKey(int key)
    {
        if (key is >= 1 and <= 9)
        {
            Selected = key - 1;
        }
        else if (key == 0)
        {
            Selected = SlotCount - 1;
        }
    }

    /// <summary>The stack in the selected slot.</summary>
    /// <param name="inventory">The inventory.</param>
    /// <returns>The stack, or the empty stack without a hotbar.</returns>
    public ItemStack SelectedStack(ClientInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return inventory.Hotbar?.Get(Selected) ?? ItemStack.Empty;
    }
}
