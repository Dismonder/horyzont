using Horyzont.Core.Ids;
using Horyzont.Core.Progress;

namespace Horyzont.Core.Operations;

/// <summary>Sets or clears (null bed) the respawn point of a player (K6-30).</summary>
/// <param name="Player">The player.</param>
/// <param name="Bed">The bed; null clears the point.</param>
public sealed record RespawnPointSetOp(PlayerId Player, BedRef? Bed) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => (OperationKind)44; // K6-30 A2: OperationKind.RespawnPointSet
}
