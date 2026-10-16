using Horyzont.Core.Items;

namespace Horyzont.Core.Survival;

/// <summary>The capacity and the drain multipliers of the suit from the modules worn (K6-34, D10); allocates nothing.</summary>
public static class SuitLoadout
{
    /// <summary>
    /// Computes the stats from the equipment container. Only the module slots (<see cref="EquipmentLayout"/>, slots 4 to 6) count and a type that is not a module is ignored.
    /// <c>Capacity = SuitBaseCapacity + the sum of the capacities</c>, at most <see cref="Protocol.ProtocolConstants.MaxSuitValue"/>;
    /// <c>Drain = max(MinDrainMultiplier, the product of the multipliers)</c>.
    /// </summary>
    /// <param name="equipment">The slots of the equipment container.</param>
    /// <param name="modules">The module table.</param>
    /// <param name="rules">The rules.</param>
    /// <returns>The stats.</returns>
    public static SuitStats Compute(ReadOnlySpan<ItemStack> equipment, SuitModuleTable modules, HazardRules rules) => throw new NotImplementedException("K6-34 A6");
}
