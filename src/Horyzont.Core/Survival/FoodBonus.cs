using Horyzont.Core.Ids;
using Horyzont.Core.Progress;

namespace Horyzont.Core.Survival;

/// <summary>The sum of the effects of the food slots at a tick (K6-33, D16).</summary>
/// <param name="Health">Extra maximum health.</param>
/// <param name="Stamina">Extra maximum stamina.</param>
/// <param name="HealthRegenPerSecond">Extra health regeneration per second.</param>
public readonly record struct FoodBonus(float Health, float Stamina, float HealthRegenPerSecond)
{
    /// <summary>Sums the effects of the three slots, each scaled by <see cref="FoodCurve.Strength"/>; allocates nothing.</summary>
    /// <param name="slots">The food slots.</param>
    /// <param name="table">The food statistics.</param>
    /// <param name="now">The current tick.</param>
    /// <param name="tickRate">Ticks per second.</param>
    /// <param name="rules">The rules.</param>
    /// <returns>The bonus; empty slots, expired food and types that are not food add nothing.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRate"/> is not positive.</exception>
    public static FoodBonus Compute(in FoodSlots slots, FoodTable table, Tick now, int tickRate, VitalsRules rules) =>
        throw new NotImplementedException("K6-33 A6");
}
