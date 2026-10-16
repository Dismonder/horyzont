using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.World;

namespace Horyzont.Core.Survival;

/// <summary>One tick of health and stamina as a pure function (K6-33, D16); allocates nothing.</summary>
public static class VitalsStep
{
    /// <summary>
    /// Advances the vitals by one tick, in this order: (1) the maxima from the food bonus; (2) the stamina cost of sprinting, jumping and tools, or,
    /// without a cost this tick and once <see cref="VitalsRules.StaminaRegenDelayTicks"/> ticks have passed since <see cref="VitalsTickInput.LastStaminaCost"/>
    /// (at once when it is <c>Tick(0)</c>), the regeneration (x1.5 while <paramref name="now"/> is before <see cref="PlayerVitals.RestedUntil"/>); (3) the health regeneration; (4) the damage; (5) both values are clamped
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
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRate"/> is not positive.</exception>
    /// <remarks>A damage or an extra cost that is negative or not finite, and a non-finite field of <paramref name="bonus"/>, count as 0 (physics and the APIs of later packages feed this).</remarks>
    public static VitalsStepResult Advance(in PlayerVitals current, in VitalsTickInput input, in FoodBonus bonus, VitalsRules rules, int tickRate, Tick now)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);

        // (1) The maxima. A non-finite or negative bonus counts as 0, and no maximum exceeds what the formats can hold.
        var healthMax = MathF.Min(rules.BaseHealth + Usable(bonus.Health), ProtocolConstants.MaxVitalValue);
        var staminaMax = MathF.Min(rules.BaseStamina + Usable(bonus.Stamina), ProtocolConstants.MaxVitalValue);
        var health = float.IsFinite(current.Health) ? current.Health : healthMax;
        var stamina = float.IsFinite(current.Stamina) ? current.Stamina : staminaMax;

        // (2) The cost of this tick, or the regeneration when nothing was spent now and the delay since the last cost is over.
        var extra = Usable(input.ExtraStaminaCost);
        var spent = input.Sprinting || input.Jumped || extra > 0f;
        if (spent)
        {
            stamina -= (input.Sprinting ? rules.SprintStaminaPerSecond / tickRate : 0f) + (input.Jumped ? rules.JumpStamina : 0f) + extra;
        }
        else if (DelayOver(input.LastStaminaCost, now, rules.StaminaRegenDelayTicks))
        {
            var perTick = rules.StaminaRegenPerSecond / tickRate;
            stamina += now < current.RestedUntil ? perTick * (1f + rules.RestStaminaRegenBonus) : perTick;
        }

        // (3) The health regeneration, (4) the damage.
        health += (rules.BaseHealthRegenPerSecond + Usable(bonus.HealthRegenPerSecond)) / tickRate;
        health -= Usable(input.Damage);

        // (5) The clamps: both values to the maxima, stamina to 0 and health to the floor that stands until death exists (K6-35).
        health = MathF.Max(MathF.Min(health, healthMax), rules.MinHealthWithoutDeath);
        stamina = MathF.Max(MathF.Min(stamina, staminaMax), 0f);

        // (6) Stamina 0 exhausts; an exhausted player recovers at the fraction of the maximum.
        var exhausted = current.Exhausted;
        if (stamina <= 0f)
        {
            exhausted = true;
        }
        else if (exhausted && stamina >= rules.ExhaustedRecoverFraction * staminaMax)
        {
            exhausted = false;
        }

        return new VitalsStepResult(new PlayerVitals(health, stamina, current.RestedUntil, exhausted), healthMax, staminaMax);
    }

    private static float Usable(float value) => float.IsFinite(value) && value > 0f ? value : 0f;

    private static bool DelayOver(Tick lastCost, Tick now, int delayTicks) =>
        lastCost.Value == 0 || (now.Value >= lastCost.Value && now.Value - lastCost.Value >= (ulong)Math.Max(delayTicks, 0));
}
