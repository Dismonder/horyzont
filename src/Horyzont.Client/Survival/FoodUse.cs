using Horyzont.Client.Crafting;
using Horyzont.Client.Inventory;
using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Survival;

namespace Horyzont.Client.Survival;

/// <summary>
/// Eating from the hotbar (K6-33 D21): a fresh press of the primary button with a food item in the selected slot, no tool in it and no prop under the crosshair builds
/// an <see cref="EatIntent"/>. The same rule the server uses, <see cref="FoodSlotRules.TryPlace"/>, runs first on the food the server told about, so a repeat of food
/// that is still strong is refused at once with code 49 and nothing is sent. A prop under the crosshair has priority (a hit, K6-31); a held button never eats.
/// Main thread only.
/// </summary>
public sealed class FoodUse
{
    private readonly ClientInventory _inventory;
    private readonly HotbarSelection _selection;
    private readonly ItemToolMap _tools;
    private readonly ClientProgress _progress;
    private readonly ServerTickReader _serverTick;
    private readonly VitalsRules _rules;
    private readonly int _tickRate;

    /// <summary>Initializes a new instance of the <see cref="FoodUse"/> class.</summary>
    /// <param name="inventory">The inventory of the local player.</param>
    /// <param name="selection">The hotbar selection.</param>
    /// <param name="tools">Which items are tools.</param>
    /// <param name="progress">The food the server told about.</param>
    /// <param name="serverTick">Where the tick of the server is read from.</param>
    /// <param name="rules">The rules of the check; the defaults when null.</param>
    /// <param name="tickRate">Ticks per second.</param>
    public FoodUse(
        ClientInventory inventory, HotbarSelection selection, ItemToolMap tools, ClientProgress progress, ServerTickReader serverTick, VitalsRules? rules = null,
        int tickRate = 60)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _serverTick = serverTick ?? throw new ArgumentNullException(nameof(serverTick));
        _rules = rules ?? VitalsRules.Default;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);
        _tickRate = tickRate;
    }

    /// <summary>Builds the intent to eat the food in the selected slot when this tick calls for it.</summary>
    /// <param name="primaryPressed">The primary button went down since the last tick (a held button does not eat).</param>
    /// <param name="propTargeted">A prop is under the crosshair (it has priority).</param>
    /// <param name="intent">The intent when the method returns true.</param>
    /// <param name="refusal"><see cref="RejectReason.FoodAlreadyActive"/> when the check of the rule refused it locally, else <see cref="RejectReason.None"/>.</param>
    /// <returns>True when <paramref name="intent"/> is to be sent.</returns>
    public bool TryBuild(bool primaryPressed, bool propTargeted, out EatIntent? intent, out RejectReason refusal)
    {
        intent = null;
        refusal = RejectReason.None;
        if (!primaryPressed || propTargeted || _inventory.Hotbar is not { } hotbar)
        {
            return false;
        }

        var stack = _selection.SelectedStack(_inventory);
        if (stack.IsEmpty || _tools.Resolve(_inventory, _selection) != ToolKind.None || !_inventory.TryGetDef(stack.Type, out var def) || def.Food is null)
        {
            return false;
        }

        if (_serverTick(out var now))
        {
            var table = FoodTable.Build(type => _inventory.TryGetDef(type, out var other) ? other.Food : null);
            var verdict = FoodSlotRules.TryPlace(_progress.Food, stack.Type, table, new Tick(now), _rules, _tickRate, out _);
            if (verdict != RejectReason.None)
            {
                refusal = verdict;
                return false;
            }
        }

        intent = new EatIntent(hotbar.Id, (ushort)_selection.Selected);
        return true;
    }
}
