using Horyzont.Core.Ids;

namespace Horyzont.Core.Checkpoints;

/// <summary>The key of a harvested prop: its planet and identifier.</summary>
/// <param name="Planet">Planet.</param>
/// <param name="Prop">The prop.</param>
public readonly record struct HarvestedPropKey(PlanetId Planet, PropId Prop);
