using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>A change of the harvested set made by an operation, as listed by <see cref="OperationProps.CollectChanges"/>.</summary>
/// <param name="Planet">Planet.</param>
/// <param name="Prop">The prop.</param>
/// <param name="Harvested">True when the prop became harvested, false when it grew back.</param>
public readonly record struct PropChange(PlanetId Planet, PropId Prop, bool Harvested);
