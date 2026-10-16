using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>A prop is harvested (or removed by digging): adds it to the harvested set (K6-31, format 1).</summary>
/// <param name="Planet">Planet.</param>
/// <param name="Prop">The prop; must not be harvested.</param>
/// <param name="Tick">Tick of the harvest, stored with the prop; the applier does not compare it with the current tick.</param>
public sealed record PropHarvestOp(PlanetId Planet, PropId Prop, Tick Tick) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => (OperationKind)48; // K6-31 A3: OperationKind.PropHarvest
}
