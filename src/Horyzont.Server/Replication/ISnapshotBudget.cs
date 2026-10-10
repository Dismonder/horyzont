using Horyzont.Core.Ids;

namespace Horyzont.Server.Replication;

/// <summary>Budget for encoded snapshots of one player.</summary>
public interface ISnapshotBudget
{
    /// <summary>Returns the available encoded byte budget.</summary>
    /// <param name="player">Recipient.</param>
    /// <param name="tick">Simulation tick.</param>
    /// <returns>Available bytes.</returns>
    int GetByteBudget(PlayerId player, Tick tick);

    /// <summary>Charges an encoded snapshot.</summary>
    /// <param name="player">Recipient.</param>
    /// <param name="encodedBytes">Actual encoded bytes.</param>
    void Debit(PlayerId player, int encodedBytes);
}
