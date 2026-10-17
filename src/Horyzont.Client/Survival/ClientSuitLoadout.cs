using Horyzont.Client.Inventory;
using Horyzont.Core.Items;
using Horyzont.Core.Survival;

namespace Horyzont.Client.Survival;

/// <summary>
/// The capacity and the drain of the suit as the client sees them (K6-34 D20): the same <see cref="SuitLoadout.Compute"/> the server uses, over the equipment container the server told about and the
/// module statistics of the item content. Rebuilds its table when the item registry changes. Main thread only; no allocation after the first call.
/// </summary>
public sealed class ClientSuitLoadout
{
    private readonly ClientInventory _inventory;
    private readonly HazardRules _rules;
    private readonly ItemStack[] _worn = new ItemStack[EquipmentLayout.SlotCount];
    private SuitModuleTable? _table;
    private int _tableVersion = -1;

    /// <summary>Initializes a new instance of the <see cref="ClientSuitLoadout"/> class.</summary>
    /// <param name="inventory">The inventory of the local player.</param>
    /// <param name="rules">The numbers of the suit, the defaults when null.</param>
    public ClientSuitLoadout(ClientInventory inventory, HazardRules? rules = null)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _rules = rules ?? HazardRules.Default;
    }

    /// <summary>Computes the capacity and the drain from the equipment as it is now (the base values without an equipment container).</summary>
    /// <returns>The statistics.</returns>
    public SuitStats Compute()
    {
        if (_table is null || _tableVersion != _inventory.RegistryVersion)
        {
            _tableVersion = _inventory.RegistryVersion;
            _table = SuitModuleTable.Build(type => _inventory.TryGetDef(type, out var def) ? def.SuitModule : null);
        }

        var equipment = _inventory.Equipment;
        for (var i = 0; i < _worn.Length; i++)
        {
            _worn[i] = equipment is null ? ItemStack.Empty : equipment.Get(i);
        }

        return SuitLoadout.Compute(_worn, _table, _rules);
    }
}
