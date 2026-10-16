using Horyzont.Core.Ids;

namespace Horyzont.Core.BlockEntities;

/// <summary>State of a bed block.</summary>
/// <param name="Owner">Owning player, <see cref="PlayerId.None"/> for a free bed.</param>
public sealed record BedBlockState(PlayerId Owner) : BlockEntityState
{
    /// <inheritdoc />
    public override BlockEntityKind Kind => BlockEntityKind.Bed;
}
