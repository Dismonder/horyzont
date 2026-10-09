using Horyzont.Core.Protocol;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Session;

/// <summary>Log entries of the client runtime.</summary>
internal static partial class ClientLog
{
    [LoggerMessage(EventId = 9001, Level = LogLevel.Information, Message = "Connecting as {Name} (host local: {IsHostLocal})")]
    public static partial void Connecting(this ILogger logger, string name, bool isHostLocal);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Information, Message = "Joined world {World} as player {Player}")]
    public static partial void Joined(this ILogger logger, Guid world, ulong player);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Warning, Message = "The server refused the connection: {Code} ({Detail})")]
    public static partial void Rejected(this ILogger logger, HandshakeRejectCode code, string detail);

    [LoggerMessage(EventId = 9004, Level = LogLevel.Information, Message = "Disconnected: {Code}")]
    public static partial void Disconnected(this ILogger logger, DisconnectCode code);
}
