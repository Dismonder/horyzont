using Horyzont.Core.Ids;
using Horyzont.Core.Progress;

namespace Horyzont.Core.Operations;

/// <summary>Sets the three food slots of a player (K6-33, operation 41); reversible, may be a component of a <see cref="CompoundOp"/>.</summary>
/// <param name="Player">The player.</param>
/// <param name="Slots">The new slots: an empty slot has tick 0, the types of the filled slots differ and are known to the world.</param>
public sealed record FoodSetOp(PlayerId Player, FoodSlots Slots) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => (OperationKind)41; // K6-33 A3: OperationKind.FoodSet
}
