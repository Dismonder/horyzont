using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>A player discovers an item type (K6-32, operation 43); reversible, may be a component of a <see cref="CompoundOp"/>.</summary>
/// <param name="Player">The player.</param>
/// <param name="Type">The item type, not 0 and known to the world.</param>
public sealed record ItemDiscoverOp(PlayerId Player, ItemTypeId Type) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.ItemDiscover;
}
