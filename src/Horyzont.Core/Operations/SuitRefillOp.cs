using Horyzont.Core.Ids;
using Horyzont.Core.Survival;

namespace Horyzont.Core.Operations;

/// <summary>
/// Adds to a suit resource of a player (K6-34, operation 42); reversible, may be a component of a <see cref="CompoundOp"/>. The new value is
/// <c>max(value, min(value + Amount, Capacity))</c>: it never lowers a value above the capacity. Nothing changes when the player has no vitals or no suit yet.
/// </summary>
/// <param name="Player">The player.</param>
/// <param name="Resource">The resource, 1 to 5.</param>
/// <param name="Amount">The points to add, 1 to <see cref="Protocol.ProtocolConstants.MaxSuitValue"/>.</param>
/// <param name="Capacity">The capacity of the resource now, 1 to <see cref="Protocol.ProtocolConstants.MaxSuitValue"/>; the validator counts it from the modules.</param>
public sealed record SuitRefillOp(PlayerId Player, SuitResource Resource, ushort Amount, ushort Capacity) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => (OperationKind)42; // K6-34 A3: OperationKind.SuitRefill
}
