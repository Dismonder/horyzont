using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>The full set of harvested props of one region (K6-31 D17); an empty list clears the region at the client.</summary>
/// <param name="Region">The region.</param>
/// <param name="Props">Harvested props of the region, ascending and unique, all inside the region.</param>
public sealed record PropRegionState(PropRegionKey Region, IReadOnlyList<HarvestedPropEntry> Props);
