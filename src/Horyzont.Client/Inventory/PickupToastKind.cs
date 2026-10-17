namespace Horyzont.Client.Inventory;

/// <summary>What a toast on the right of the screen says.</summary>
public enum PickupToastKind : byte
{
    /// <summary>Items were added to the inventory ("+12 Stone").</summary>
    Pickup = 1,

    /// <summary>A dig was accepted but the items did not fit ("Inventory full").</summary>
    InventoryFull = 2,
}
