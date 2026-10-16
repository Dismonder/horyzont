using Horyzont.Core.Ids;
using Horyzont.Core.World;

namespace Horyzont.Core.Survival;

/// <summary>One tick of health and stamina as a pure function (K6-33, D16); allocates nothing.</summary>
public static class VitalsStep
{
    /// <summary>
    /// Advances the vitals by one tick, in this order: (1) the maxima from the food bonus; (2) the stamina cost of sprinting, jumping and tools, or,
    /// without a cost this tick and without <see cref="VitalsTickInput.StaminaUsedRecently"/>, the regeneration (x1.5 while
    /// <paramref name="now"/> is before <see cref="PlayerVitals.RestedUntil"/>); (3) the health regeneration; (4) the damage; (5) both values are clamped
    /// to the maxima and health to at least <see cref="VitalsRules.MinHealthWithoutDeath"/>; (6) stamina 0 exhausts, and an exhausted player recovers at
    /// <see cref="VitalsRules.ExhaustedRecoverFraction"/> of the maximum.
    /// </summary>
    /// <param name="current">The vitals before the tick.</param>
    /// <param name="input">What happened during the tick.</param>
    /// <param name="bonus">The food bonus at this tick.</param>
    /// <param name="rules">The rules.</param>
    /// <param name="tickRate">Ticks per second.</param>
    /// <param name="now">The current tick.</param>
    /// <returns>The new vitals and the maxima.</returns>
    public static VitalsStepResult Advance(in PlayerVitals current, in VitalsTickInput input, in FoodBonus bonus, VitalsRules rules, int tickRate, Tick now) =>
        throw new NotImplementedException("K6-33 A6");
}
