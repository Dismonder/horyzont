using Horyzont.Client.Inventory;
using Horyzont.Core.Intents;

namespace Horyzont.Client.Survival;

/// <summary>
/// Refilling the suit from the hotbar (K6-34 D20): a fresh press of the primary button with a suit refill in the selected slot, no tool in it and no prop under the crosshair builds a
/// <see cref="SuitRefillIntent"/>, unless the resource it refills is already full by the last message 44 (then nothing is sent and the caller shows the local text). The server decides in the end:
/// it accepts a refill while the resource is below its capacity, so the "full" threshold of the client (one point below the capacity) is a courtesy (review B5, P3-8). Main thread only.
/// </summary>
public sealed class SuitRefillUse
{
    /// <summary>The distance from the capacity at which the client calls a resource full.</summary>
    public const float FullMargin = 1f;

    private readonly ClientInventory _inventory;
    private readonly HotbarSelection _selection;
    private readonly ItemToolMap _tools;
    private readonly ClientVitals _vitals;

    /// <summary>Initializes a new instance of the <see cref="SuitRefillUse"/> class.</summary>
    /// <param name="inventory">The inventory of the local player.</param>
    /// <param name="selection">The hotbar selection.</param>
    /// <param name="tools">Which items are tools.</param>
    /// <param name="vitals">The vitals the server told about.</param>
    public SuitRefillUse(ClientInventory inventory, HotbarSelection selection, ItemToolMap tools, ClientVitals vitals)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _vitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
    }

    /// <summary>Builds the intent to refill the suit from the selected slot when this tick calls for it.</summary>
    /// <param name="primaryPressed">The primary button went down since the last tick (a held button does not refill).</param>
    /// <param name="propTargeted">A prop is under the crosshair (it has priority).</param>
    /// <param name="intent">The intent when the method returns true.</param>
    /// <param name="resourceFull">True when the selected item is a refill whose resource is full, so nothing is sent.</param>
    /// <returns>True when <paramref name="intent"/> is to be sent.</returns>
    public bool TryBuild(bool primaryPressed, bool propTargeted, out SuitRefillIntent? intent, out bool resourceFull)
    {
        intent = null;
        resourceFull = false;
        if (!primaryPressed || propTargeted || _inventory.Hotbar is not { } hotbar)
        {
            return false;
        }

        var stack = _selection.SelectedStack(_inventory);
        if (stack.IsEmpty || _tools.Resolve(_inventory, _selection) != Tools.ToolKind.None || !_inventory.TryGetDef(stack.Type, out var def) || def.SuitRefill is not { } refill)
        {
            return false;
        }

        if (_vitals.HasState && _vitals.Suit[refill.Resource] > _vitals.SuitCapacity[refill.Resource] - FullMargin)
        {
            resourceFull = true;
            return false;
        }

        intent = new SuitRefillIntent(hotbar.Id, (ushort)_selection.Selected);
        return true;
    }
}
