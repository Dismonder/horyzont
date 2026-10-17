using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Operations;

/// <summary>Creates an empty container. Format 1 accepts only <see cref="ContainerHostKind.Player"/> hosts when applied.</summary>
/// <param name="Id">New container id.</param>
/// <param name="ContainerKind">Kind of the container (<see cref="Operation.Kind"/> is the operation code).</param>
/// <param name="Host">Owner of the container; a player host may have one container of each kind.</param>
/// <param name="Owner">Owning player, <see cref="PlayerId.None"/> for none.</param>
/// <param name="Access">Access.</param>
/// <param name="Size">Number of slots, 1..256.</param>
public sealed record ContainerCreateOp(
    ContainerId Id, ContainerKind ContainerKind, ContainerHost Host, PlayerId Owner, ContainerAccess Access, ushort Size) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.ContainerCreate;
}
