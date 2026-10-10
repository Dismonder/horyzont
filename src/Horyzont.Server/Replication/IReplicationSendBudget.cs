using Horyzont.Core.Ids;

namespace Horyzont.Server.Replication;

/// <summary>Shared budget for reliable entity and terrain replication, used on the main thread.</summary>
public interface IReplicationSendBudget
{
    /// <summary>Charges an allowed transfer, permitting live traffic to incur debt.</summary>
    /// <param name="player">Recipient.</param>
    /// <param name="traffic">Traffic class.</param>
    /// <param name="bytes">Actual encoded bytes.</param>
    /// <returns>Whether the transfer may proceed.</returns>
    bool TryConsume(PlayerId player, ReplicationTraffic traffic, int bytes);
}
