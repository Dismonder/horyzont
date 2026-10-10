using Horyzont.Core.Ids;

namespace Horyzont.Server.Replication;

/// <summary>Copies authoritative main-thread entity and kinematic data, without a dependency on the engine.</summary>
public interface IEntityReplicationSource
{
    /// <summary>Copies entities sorted by identifier; a full buffer asks the caller to grow and retry.</summary>
    /// <param name="destination">Reusable storage.</param>
    /// <returns>Number copied.</returns>
    int CopyEntities(Span<ReplicationEntity> destination);

    /// <summary>Last input processed by the authoritative character.</summary>
    /// <param name="player">Player.</param>
    /// <returns>Input sequence.</returns>
    uint GetLastProcessedInputSeq(PlayerId player);
}
