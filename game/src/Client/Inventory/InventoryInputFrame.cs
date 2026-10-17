namespace Horyzont.Game.Client.Inventory;

/// <summary>Input of the inventory since the previous frame.</summary>
/// <param name="HotbarKey">Number key pressed (1 to 9, 0), or -1.</param>
/// <param name="WheelSteps">Hotbar steps from the mouse wheel; positive selects the next slot.</param>
/// <param name="Toggle">The inventory key (Tab or I) was pressed.</param>
/// <param name="Escape">Escape was pressed.</param>
public readonly record struct InventoryInputFrame(int HotbarKey, int WheelSteps, bool Toggle, bool Escape)
{
    /// <summary>No input.</summary>
    public static InventoryInputFrame None => new(-1, 0, false, false);
}
