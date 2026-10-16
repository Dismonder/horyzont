namespace Horyzont.Core.Survival;

/// <summary>What the surroundings of a resting player offer (K6-33 D14).</summary>
/// <param name="Roofed">Whether a construct or the terrain lies above the point within the roof ray.</param>
/// <param name="Heated">Whether a block with a heat source lies within the heat range.</param>
/// <param name="Comfort">Sum of the comfort of the different block types within the comfort range, capped at the maximum.</param>
public readonly record struct RestConditions(bool Roofed, bool Heated, byte Comfort);
