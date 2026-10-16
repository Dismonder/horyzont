using Horyzont.Core.Ids;

namespace Horyzont.Core.Survival;

/// <summary>What happened to a player during one tick (K6-33, D15), as input of <see cref="VitalsStep.Advance"/>.</summary>
/// <param name="Sprinting">The body sprinted this tick.</param>
/// <param name="Jumped">The body started a jump this tick.</param>
/// <param name="ExtraStaminaCost">Stamina spent this tick on top of sprinting and jumping (tool use).</param>
/// <param name="Damage">Health damage taken this tick.</param>
/// <param name="LastStaminaCost">The tick stamina was last spent at (before this tick), <c>Tick(0)</c> for never; regeneration resumes <see cref="VitalsRules.StaminaRegenDelayTicks"/> ticks after it.</param>
public readonly record struct VitalsTickInput(bool Sprinting, bool Jumped, float ExtraStaminaCost, float Damage, Tick LastStaminaCost);
