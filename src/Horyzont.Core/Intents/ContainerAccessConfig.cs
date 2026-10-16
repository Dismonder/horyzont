using Horyzont.Core.Items;

namespace Horyzont.Core.Intents;

/// <summary>A new access of a chest container.</summary>
/// <param name="Access">The access.</param>
public sealed record ContainerAccessConfig(ContainerAccess Access) : BlockConfig
{
    /// <inheritdoc />
    public override BlockConfigKind Kind => BlockConfigKind.ContainerAccess;
}
