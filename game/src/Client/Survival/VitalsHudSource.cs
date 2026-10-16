using Horyzont.Client.Crafting;
using Horyzont.Client.Inventory;
using Horyzont.Client.Survival;
using Horyzont.Core.Ids;
using Horyzont.Core.Progress;
using Horyzont.Core.Survival;
using Horyzont.Game.Client.Inventory;

namespace Horyzont.Game.Client.Survival;

/// <summary>
/// Builds the data of the vitals HUD from what the client knows (K6-33): the vitals of message 44, the food slots of message 48, the item content and the tick of the
/// server. The food time counts down with the tick of the server, so the HUD needs no message to move. Main thread only; the arrays are reused.
/// </summary>
public sealed class VitalsHudSource
{
    private readonly ClientVitals _vitals;
    private readonly ClientProgress _progress;
    private readonly ClientInventory _inventory;
    private readonly Func<ItemTypeId, ItemVisual> _visual;
    private readonly Func<ItemTypeId, string> _name;
    private readonly ServerTickReader _serverTick;
    private readonly VitalsRules _rules;
    private readonly int _tickRate;
    private readonly FoodSlotData[] _food = new FoodSlotData[FoodSlots.Count];

    /// <summary>Initializes a new instance of the <see cref="VitalsHudSource"/> class.</summary>
    /// <param name="vitals">The vitals of the local player.</param>
    /// <param name="progress">The progress with the food slots.</param>
    /// <param name="inventory">The inventory (the food statistics come from its item content).</param>
    /// <param name="visual">The icon of an item type.</param>
    /// <param name="name">The name of an item type.</param>
    /// <param name="serverTick">Where the tick of the server is read from.</param>
    /// <param name="rules">The rules (the repeat fraction of the blinking); the defaults when null.</param>
    /// <param name="tickRate">Ticks per second.</param>
    public VitalsHudSource(
        ClientVitals vitals, ClientProgress progress, ClientInventory inventory, Func<ItemTypeId, ItemVisual> visual, Func<ItemTypeId, string> name,
        ServerTickReader serverTick, VitalsRules? rules = null, int tickRate = 60)
    {
        _vitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _visual = visual ?? throw new ArgumentNullException(nameof(visual));
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _serverTick = serverTick ?? throw new ArgumentNullException(nameof(serverTick));
        _rules = rules ?? VitalsRules.Default;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);
        _tickRate = tickRate;
    }

    /// <summary>Builds the data of the current frame.</summary>
    /// <returns>The data; not visible until the server has sent the vitals.</returns>
    public VitalsHudData Build()
    {
        var known = _serverTick(out var tick);
        var now = new Tick(known ? tick : 0);
        for (var i = 0; i < FoodSlots.Count; i++)
        {
            _food[i] = FoodOf(_progress.Food[i], now, known);
        }

        var rested = _vitals.RestRemainingTicks(tick);
        return new VitalsHudData(
            _vitals.HasState, _vitals.Health, _vitals.HealthMax, _vitals.Stamina, _vitals.StaminaMax, _vitals.Exhausted, _vitals.Resting,
            _vitals.RestWarmupTicks / _tickRate, _rules.RestWarmupTicks / _tickRate, rested > 0 ? VitalsHudText.Duration(rested, _tickRate) : string.Empty, _vitals.Comfort, _food);
    }

    private FoodSlotData FoodOf(FoodSlot slot, Tick now, bool known)
    {
        if (slot.IsEmpty || !known || !_inventory.TryGetDef(slot.Type, out var def) || def.Food is not { } food)
        {
            return default;
        }

        var fraction = VitalsHudText.FoodFraction(slot, food, now, _tickRate);
        if (fraction <= 0.0)
        {
            return default;
        }

        var ticks = VitalsHudText.FoodRemainingTicks(slot, food, now, _tickRate);
        return new FoodSlotData(true, _visual(slot.Type), _name(slot.Type), fraction, VitalsHudText.Duration(ticks, _tickRate), fraction <= _rules.FoodRepeatFraction);
    }
}
