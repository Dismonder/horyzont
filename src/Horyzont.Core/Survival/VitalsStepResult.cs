using Horyzont.Core.World;

namespace Horyzont.Core.Survival;

/// <summary>The result of one vitals tick (K6-33, D16).</summary>
/// <param name="Vitals">The new vitals.</param>
/// <param name="HealthMax">The maximum health at this tick.</param>
/// <param name="StaminaMax">The maximum stamina at this tick.</param>
public readonly record struct VitalsStepResult(PlayerVitals Vitals, float HealthMax, float StaminaMax);
