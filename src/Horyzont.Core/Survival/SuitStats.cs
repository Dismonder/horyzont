namespace Horyzont.Core.Survival;

/// <summary>The capacity and the drain multiplier of every suit resource under a loadout (K6-34, D10); both are indexed by <see cref="SuitResource"/>.</summary>
/// <param name="Capacity">The capacity of each resource, 100 plus the modules, at most <see cref="Protocol.ProtocolConstants.MaxSuitValue"/>.</param>
/// <param name="Drain">The multiplier of the drain of each resource, at least <see cref="HazardRules.MinDrainMultiplier"/>, 1 without modules.</param>
public readonly record struct SuitStats(SuitResources Capacity, SuitResources Drain);
