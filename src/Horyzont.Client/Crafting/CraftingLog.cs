using Horyzont.Core.Intents;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Crafting;

/// <summary>Log entries of the client crafting (K6-32).</summary>
internal static partial class CraftingLog
{
    [LoggerMessage(EventId = 9320, Level = LogLevel.Debug, Message = "Craft intent {ClientSeq} sent: recipe {Recipe} x{Count}")]
    public static partial void CraftSent(this ILogger logger, uint clientSeq, string recipe, ushort count);

    [LoggerMessage(EventId = 9321, Level = LogLevel.Information, Message = "The server refused craft intent {ClientSeq}: {Reason}")]
    public static partial void CraftRefused(this ILogger logger, uint clientSeq, RejectReason reason);

    [LoggerMessage(EventId = 9322, Level = LogLevel.Debug, Message = "Station of construct {Construct} requested: containers {Input} and {Output}")]
    public static partial void StationRequested(this ILogger logger, ulong construct, ulong input, ulong output);

    [LoggerMessage(EventId = 9323, Level = LogLevel.Debug, Message = "Station of construct {Construct} opened")]
    public static partial void StationOpened(this ILogger logger, ulong construct);

    [LoggerMessage(EventId = 9324, Level = LogLevel.Debug, Message = "Station of construct {Construct} closed by the player")]
    public static partial void StationClosed(this ILogger logger, ulong construct);

    [LoggerMessage(EventId = 9325, Level = LogLevel.Information, Message = "Station container {Container} refused or ended by the server: {Reason}")]
    public static partial void StationRefused(this ILogger logger, ulong container, RejectReason reason);
}
