using Horyzont.Core.Intents;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Inventory;

/// <summary>Log entries of the inventory service; each names the player and, for operations, the operation id.</summary>
internal static partial class InventoryLog
{
    [LoggerMessage(EventId = 8401, Level = LogLevel.Information, Message = "Created the containers of player {PlayerId} in operation {OpId} ({Items} start kit items)")]
    public static partial void ContainersCreated(this ILogger logger, ulong playerId, ulong opId, int items);

    [LoggerMessage(EventId = 8402, Level = LogLevel.Error, Message = "Could not create the containers of player {PlayerId}; the player plays without an inventory")]
    public static partial void ContainerCreationFailed(this ILogger logger, ulong playerId, Exception exception);

    [LoggerMessage(EventId = 8403, Level = LogLevel.Warning, Message = "Player {PlayerId} has no containers and the server is read-only; nothing is created")]
    public static partial void ReadOnlyNoContainers(this ILogger logger, ulong playerId);

    [LoggerMessage(EventId = 8404, Level = LogLevel.Error, Message = "The start kit item '{ItemKey}' of player {PlayerId} is not in the item content or the world registry; it is skipped")]
    public static partial void StartKitItemUnknown(this ILogger logger, ulong playerId, string itemKey);

    [LoggerMessage(EventId = 8405, Level = LogLevel.Debug, Message = "Player {PlayerId} subscribed to container {ContainerId}: {Result}")]
    public static partial void SubscribeHandled(this ILogger logger, ulong playerId, ulong containerId, string result);

    [LoggerMessage(EventId = 8406, Level = LogLevel.Debug, Message = "Sent a delta of container {ContainerId} version {Version} to player {PlayerId} for operation {OpId}")]
    public static partial void DeltaSent(this ILogger logger, ulong playerId, ulong containerId, uint version, ulong opId);

    [LoggerMessage(EventId = 8407, Level = LogLevel.Warning, Message = "Player {PlayerId} is missing some of its containers; the existing ones are sent")]
    public static partial void ContainersIncomplete(this ILogger logger, ulong playerId);

    [LoggerMessage(EventId = 8408, Level = LogLevel.Debug, Message = "Ended the subscription of player {PlayerId} to chest container {ContainerId}: {Reason}")]
    public static partial void SubscriptionEnded(this ILogger logger, ulong playerId, ulong containerId, RejectReason reason);
}
