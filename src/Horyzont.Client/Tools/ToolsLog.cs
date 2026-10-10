using Horyzont.Core.Intents;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Tools;

/// <summary>Log entries of the client tools.</summary>
internal static partial class ToolsLog
{
    [LoggerMessage(EventId = 9201, Level = LogLevel.Information, Message = "The server rejected intent {ClientSeq}: {Reason}")]
    public static partial void IntentRejected(this ILogger logger, uint clientSeq, RejectReason reason);

    [LoggerMessage(EventId = 9202, Level = LogLevel.Warning, Message = "Reply to unknown or repeated intent {ClientSeq} ignored ({Reason})")]
    public static partial void UnknownReply(this ILogger logger, uint clientSeq, RejectReason reason);

    [LoggerMessage(EventId = 9203, Level = LogLevel.Warning, Message = "No reply to intent {ClientSeq} after {AgeTicks} ticks, the preview is reverted")]
    public static partial void IntentTimedOut(this ILogger logger, uint clientSeq, long ageTicks);

    [LoggerMessage(EventId = 9204, Level = LogLevel.Information, Message = "{Count} pending intents dropped on disconnect")]
    public static partial void PendingCleared(this ILogger logger, int count);

    [LoggerMessage(EventId = 9205, Level = LogLevel.Debug, Message = "Intent {ClientSeq} sent: {Intent}")]
    public static partial void IntentSent(this ILogger logger, uint clientSeq, Intent intent);

    [LoggerMessage(EventId = 9206, Level = LogLevel.Debug, Message = "Action refused locally: {MaxPending} intents wait for the server")]
    public static partial void TooManyPending(this ILogger logger, int maxPending);
}
