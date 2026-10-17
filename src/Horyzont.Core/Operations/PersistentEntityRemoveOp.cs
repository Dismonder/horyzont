using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>Removes a persistent entity that no longer hosts a container (K6-35, operation 39).</summary>
/// <param name="Entity">The entity.</param>
public sealed record PersistentEntityRemoveOp(EntityId Entity) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.PersistentEntityRemove;
}
