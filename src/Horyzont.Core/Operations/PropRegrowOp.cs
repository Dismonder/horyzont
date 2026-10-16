using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>A harvested prop grows back: removes it from the harvested set (K6-31, server operation, format 1).</summary>
/// <param name="Planet">Planet.</param>
/// <param name="Prop">The prop; must be harvested.</param>
public sealed record PropRegrowOp(PlanetId Planet, PropId Prop) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => (OperationKind)47; // K6-31 A3: OperationKind.PropRegrow
}
