namespace Horyzont.Core.Survival;

/// <summary>The result of one suit tick (K6-34, D10).</summary>
/// <param name="Next">The resources after the tick.</param>
/// <param name="Damage">The health lost during the tick to the resources at zero.</param>
/// <param name="ExposedMask">Bit <c>r - 1</c> is set when the resource <c>r</c> is at zero while its hazard is active.</param>
public readonly record struct SuitStepResult(SuitResources Next, float Damage, byte ExposedMask);
