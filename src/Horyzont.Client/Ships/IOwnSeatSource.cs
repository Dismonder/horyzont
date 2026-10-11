using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Ships;

/// <summary>
/// The seat of the local player. K5-26 replicates it in <c>EntitySpawn.KindData</c> (also after a rejoin); until K5-26 is merged
/// <see cref="OwnSeat"/> follows the own <c>EntityReparentOp</c> messages (K5-25 decision 20, P7).
/// </summary>
public interface IOwnSeatSource
{
    /// <summary>The construct and cell of the own seat.</summary>
    /// <param name="construct">Receives the construct.</param>
    /// <param name="cell">Receives the seat cell.</param>
    /// <returns>False when the local player does not sit.</returns>
    bool TryGetOwnSeat(out EntityId construct, out CellPos cell);
}
