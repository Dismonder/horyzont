using Horyzont.Core.Items;
using Horyzont.Core.Protocol;

namespace Horyzont.Core.Survival;

/// <summary>The capacity and the drain multipliers of the suit from the modules worn (K6-34, D10); allocates nothing.</summary>
public static class SuitLoadout
{
    private const int FirstModuleSlot = 4;

    /// <summary>
    /// Computes the stats from the equipment container. Only the module slots (<see cref="EquipmentLayout"/>, slots 4 to 6) count and a type that is not a module is ignored;
    /// a stack counts once whatever its size.
    /// <c>Capacity = SuitBaseCapacity + the sum of the capacities</c>, at most <see cref="Protocol.ProtocolConstants.MaxSuitValue"/>;
    /// <c>Drain = max(MinDrainMultiplier, the product of the multipliers)</c>.
    /// </summary>
    /// <param name="equipment">The slots of the equipment container.</param>
    /// <param name="modules">The module table.</param>
    /// <param name="rules">The rules.</param>
    /// <returns>The stats.</returns>
    public static SuitStats Compute(ReadOnlySpan<ItemStack> equipment, SuitModuleTable modules, HazardRules rules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(rules);

        // Index 0 is unused so that the resource byte (1 to 5) is the index.
        Span<float> capacity = stackalloc float[6];
        Span<float> drain = stackalloc float[6];
        for (var i = 1; i < capacity.Length; i++)
        {
            capacity[i] = rules.SuitBaseCapacity;
            drain[i] = 1f;
        }

        var last = Math.Min(equipment.Length, EquipmentLayout.SlotCount);
        for (var slot = FirstModuleSlot; slot < last; slot++)
        {
            if (equipment[slot].IsEmpty || !modules.TryGet(equipment[slot].Type, out var module))
            {
                continue;
            }

            var effects = module.Effects;
            for (var e = 0; e < effects.Count; e++)
            {
                var effect = effects[e];
                var index = (int)effect.Resource;
                if (index is < 1 or > 5)
                {
                    continue;
                }

                capacity[index] += effect.Capacity;
                drain[index] *= effect.DrainMultiplier;
            }
        }

        for (var i = 1; i < capacity.Length; i++)
        {
            capacity[i] = MathF.Min(capacity[i], ProtocolConstants.MaxSuitValue);
            drain[i] = MathF.Max(rules.MinDrainMultiplier, drain[i]);
        }

        return new SuitStats(
            new SuitResources(capacity[1], capacity[2], capacity[3], capacity[4], capacity[5]),
            new SuitResources(drain[1], drain[2], drain[3], drain[4], drain[5]));
    }
}
