using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>Removes a map marker (K5-27); the id counter does not move back.</summary>
/// <param name="Marker">Marker id.</param>
public sealed record MarkerRemoveOp(MarkerId Marker) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.MarkerRemove;
}
