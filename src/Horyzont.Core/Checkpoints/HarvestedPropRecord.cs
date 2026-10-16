using Horyzont.Core.Ids;

namespace Horyzont.Core.Checkpoints;

/// <summary>A harvested prop in a checkpoint (table <c>harvested_props</c>, K6-31).</summary>
/// <param name="Planet">Planet.</param>
/// <param name="Prop">The prop.</param>
/// <param name="Tick">Tick of the harvest.</param>
public readonly record struct HarvestedPropRecord(PlanetId Planet, PropId Prop, Tick Tick);
