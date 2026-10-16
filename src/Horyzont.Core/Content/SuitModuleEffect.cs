using Horyzont.Core.Survival;

namespace Horyzont.Core.Content;

/// <summary>The effect of a suit module on one resource (K6-34, D8).</summary>
/// <param name="Resource">The resource.</param>
/// <param name="Capacity">Extra capacity, 0 to 500.</param>
/// <param name="DrainMultiplier">Multiplier of the drain, 0.1 to 1.</param>
public sealed record SuitModuleEffect(SuitResource Resource, ushort Capacity, float DrainMultiplier);
