using Horyzont.Core.Ids;

namespace Horyzont.Core.BlockEntities;

/// <summary>State of a chest block.</summary>
/// <param name="Container">Container hosted by the block.</param>
public sealed record ChestBlockState(ContainerId Container) : BlockEntityState
{
    /// <inheritdoc />
    public override BlockEntityKind Kind => BlockEntityKind.Chest;
}
