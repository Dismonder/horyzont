using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Replication;

/// <summary>Log entries of entity and construct replication; entries name the player and the entity.</summary>
internal static partial class ReplicationLog
{
    [LoggerMessage(EventId = 8301, Level = LogLevel.Debug, Message = "Player {Player} acknowledged unknown snapshot {Snapshot} ({Count} so far)")]
    public static partial void UnknownAck(this ILogger logger, ulong player, uint snapshot, int count);

    [LoggerMessage(EventId = 8302, Level = LogLevel.Debug, Message = "Player {Player} resync of entity {Entity} refused: {Reason}")]
    public static partial void ResyncRefused(this ILogger logger, ulong player, ulong entity, string reason);

    [LoggerMessage(EventId = 8303, Level = LogLevel.Information, Message = "Player {Player} resync of entity {Entity} accepted")]
    public static partial void ResyncAccepted(this ILogger logger, ulong player, ulong entity);

    [LoggerMessage(EventId = 8304, Level = LogLevel.Warning, Message = "Player {Player}: buffered operation for construct {Construct} has version {Version} after {Sent}; restarting its state")]
    public static partial void BufferedOperationGap(this ILogger logger, ulong player, ulong construct, uint version, uint sent);

    [LoggerMessage(EventId = 8305, Level = LogLevel.Warning, Message = "The replication source returned more than {Capacity} entities; the buffer is full")]
    public static partial void SourceOverflow(this ILogger logger, int capacity);
}
