using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>One harvested prop in <see cref="PropRegionState"/> (K6-31 D17).</summary>
/// <param name="Prop">The prop.</param>
/// <param name="Tick">Tick of the harvest.</param>
public readonly record struct HarvestedPropEntry(PropId Prop, Tick Tick);
