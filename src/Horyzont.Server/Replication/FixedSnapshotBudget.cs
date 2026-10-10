using Horyzont.Core.Ids;

namespace Horyzont.Server.Replication;

/// <summary>Neutral K2 snapshot budget without rate accounting.</summary>
public sealed class FixedSnapshotBudget : ISnapshotBudget
{
    /// <inheritdoc />
    public int GetByteBudget(PlayerId player, Tick tick) => 1150;

    /// <inheritdoc />
    public void Debit(PlayerId player, int encodedBytes)
    {
    }
}
