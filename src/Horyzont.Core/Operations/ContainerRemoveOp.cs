using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>Removes an empty container that is not hosted by a player (K6-30).</summary>
/// <param name="Container">The container.</param>
public sealed record ContainerRemoveOp(ContainerId Container) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.ContainerRemove;
}
