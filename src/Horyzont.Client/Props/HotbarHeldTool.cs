using Horyzont.Client.Inventory;
using Horyzont.Core.Content;

namespace Horyzont.Client.Props;

/// <summary>
/// The tool in the selected hotbar slot (K6-31 D9): the tier of the item when it carries the tool flag, 0 (the hand) for an empty slot, a
/// resource or any other item, exactly what the server reads from the slot named in the hit (<c>ToolTiers.OfStack</c>). Main thread only;
/// reads the inventory at every query, so a slot changed by the server counts at once.
/// </summary>
/// <param name="inventory">The inventory of the local player.</param>
/// <param name="selection">The hotbar selection.</param>
public sealed class HotbarHeldTool(ClientInventory inventory, HotbarSelection selection) : IHeldTool
{
    private readonly ClientInventory _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    private readonly HotbarSelection _selection = selection ?? throw new ArgumentNullException(nameof(selection));

    /// <inheritdoc />
    public int HotbarSlot => _selection.Selected;

    /// <inheritdoc />
    public int ToolTier
    {
        get
        {
            var stack = _selection.SelectedStack(_inventory);
            return !stack.IsEmpty && _inventory.TryGetDef(stack.Type, out var def) && def.Flags.HasFlag(ItemFlags.Tool) ? def.ToolTier : 0;
        }
    }
}
