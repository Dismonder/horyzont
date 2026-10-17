using Horyzont.Core.Intents;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Inventory;

/// <summary>Log entries of the client inventory.</summary>
internal static partial class InventoryLog
{
    [LoggerMessage(EventId = 9301, Level = LogLevel.Warning, Message = "Container {Container} delta version {Version} does not follow {Current}: asking for the state again")]
    public static partial void DeltaGap(this ILogger logger, ulong container, uint version, uint current);

    [LoggerMessage(EventId = 9302, Level = LogLevel.Debug, Message = "Container {Container} delta version {Version} is not newer than {Current}: ignored")]
    public static partial void DeltaOld(this ILogger logger, ulong container, uint version, uint current);

    [LoggerMessage(EventId = 9303, Level = LogLevel.Warning, Message = "Delta for unknown container {Container} ignored")]
    public static partial void DeltaUnknownContainer(this ILogger logger, ulong container);

    [LoggerMessage(EventId = 9304, Level = LogLevel.Information, Message = "Container {Container} state refused: {Reason}")]
    public static partial void StateRefused(this ILogger logger, ulong container, RejectReason reason);

    [LoggerMessage(EventId = 9305, Level = LogLevel.Debug, Message = "Container {Container} state applied: version {Version}, {Size} slots")]
    public static partial void StateApplied(this ILogger logger, ulong container, uint version, int size);

    [LoggerMessage(EventId = 9306, Level = LogLevel.Warning, Message = "Container {Container} message names slot {Slot} outside {Size} slots: ignored")]
    public static partial void SlotOutside(this ILogger logger, ulong container, int slot, int size);

    [LoggerMessage(EventId = 9307, Level = LogLevel.Debug, Message = "Inventory intent {ClientSeq} sent: {Intent}")]
    public static partial void IntentSent(this ILogger logger, uint clientSeq, Intent intent);

    [LoggerMessage(EventId = 9308, Level = LogLevel.Information, Message = "The server refused inventory intent {ClientSeq}: {Reason}")]
    public static partial void IntentRefused(this ILogger logger, uint clientSeq, RejectReason reason);

    [LoggerMessage(EventId = 9309, Level = LogLevel.Information, Message = "Item registry received: {Count} types, the largest id {MaxId}")]
    public static partial void RegistryReceived(this ILogger logger, int count, ushort maxId);
}
