namespace Horyzont.Core.Survival;

/// <summary>What happened to a player during one tick (K6-33, D15), as input of <see cref="VitalsStep.Advance"/>.</summary>
/// <param name="Sprinting">The body sprinted this tick.</param>
/// <param name="Jumped">The body started a jump this tick.</param>
/// <param name="ExtraStaminaCost">Stamina spent this tick on top of sprinting and jumping (tool use).</param>
/// <param name="Damage">Health damage taken this tick.</param>
/// <param name="StaminaUsedRecently">Stamina was spent within the last <see cref="VitalsRules.StaminaRegenDelayTicks"/> ticks.</param>
public readonly record struct VitalsTickInput(bool Sprinting, bool Jumped, float ExtraStaminaCost, float Damage, bool StaminaUsedRecently);
