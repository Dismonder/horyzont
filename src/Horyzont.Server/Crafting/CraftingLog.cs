using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Crafting;

/// <summary>Log entries of the crafting services (K6-32); each names the player and, for operations, the operation id.</summary>
internal static partial class CraftingLog
{
    [LoggerMessage(EventId = 8501, Level = LogLevel.Debug, Message = "Settled a station of construct {ConstructId} in operation {OpId}")]
    public static partial void StationSettled(this ILogger logger, ulong constructId, ulong opId);

    [LoggerMessage(EventId = 8502, Level = LogLevel.Debug, Message = "Sent the progress state to player {PlayerId}: {Discovered} discovered item types, {Keys} progress keys")]
    public static partial void ProgressSent(this ILogger logger, ulong playerId, int discovered, int keys);

    [LoggerMessage(EventId = 8503, Level = LogLevel.Debug, Message = "Discovered the items held by player {PlayerId} in operation {OpId}")]
    public static partial void HeldItemsDiscovered(this ILogger logger, ulong playerId, ulong opId);
}
