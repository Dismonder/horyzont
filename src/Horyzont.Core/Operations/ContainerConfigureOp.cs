using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Operations;

/// <summary>Changes the size or the access of a container (K6-30).</summary>
/// <param name="Container">The container.</param>
/// <param name="Size">New number of slots, 1..256; slots at or above it must be empty.</param>
/// <param name="Access">New access.</param>
public sealed record ContainerConfigureOp(ContainerId Container, ushort Size, ContainerAccess Access) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.ContainerConfigure;
}
