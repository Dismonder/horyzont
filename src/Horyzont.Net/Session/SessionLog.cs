using Horyzont.Core.Protocol;

using Microsoft.Extensions.Logging;

namespace Horyzont.Net.Session;

/// <summary>Log entries of the sessions; every server entry names the player (0 before the handshake) and the connection.</summary>
internal static partial class SessionLog
{
    [LoggerMessage(EventId = 7001, Level = LogLevel.Warning, Message = "Protocol violation from player {Player} (connection {Connection}): {Reason}")]
    public static partial void ProtocolViolation(this ILogger logger, ulong player, ulong connection, string reason);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Error, Message = "Handling traffic of player {Player} (connection {Connection}) failed; disconnecting")]
    public static partial void PeerFailed(this ILogger logger, ulong player, ulong connection, Exception exception);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Information, Message = "Handshake of connection {Connection} rejected: {Code}")]
    public static partial void HandshakeRejected(this ILogger logger, ulong connection, HandshakeRejectCode code);

    [LoggerMessage(EventId = 7004, Level = LogLevel.Information, Message = "Player {Player} (connection {Connection}) joined")]
    public static partial void PlayerJoined(this ILogger logger, ulong player, ulong connection);

    [LoggerMessage(EventId = 7005, Level = LogLevel.Information, Message = "Player {Player} (connection {Connection}) left: {Reason}")]
    public static partial void PlayerLeft(this ILogger logger, ulong player, ulong connection, DisconnectCode reason);

    [LoggerMessage(EventId = 7006, Level = LogLevel.Warning, Message = "Player {Player} (connection {Connection}) exceeded the message rate limit; kicking")]
    public static partial void Flooding(this ILogger logger, ulong player, ulong connection);

    [LoggerMessage(EventId = 7007, Level = LogLevel.Information, Message = "Connection {Connection} closing: {Code}")]
    public static partial void Closing(this ILogger logger, ulong connection, DisconnectCode code);

    [LoggerMessage(EventId = 7008, Level = LogLevel.Error, Message = "OnPlayerLeft of player {Player} (connection {Connection}) failed")]
    public static partial void LeftHookFailed(this ILogger logger, ulong player, ulong connection, Exception exception);

    [LoggerMessage(EventId = 7009, Level = LogLevel.Information, Message = "Player {Player} (connection {Connection}) sent Disconnect({Code}); treated as Normal")]
    public static partial void ForeignDisconnectCode(this ILogger logger, ulong player, ulong connection, DisconnectCode code);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Warning, Message = "Protocol violation by the server: {Reason}")]
    public static partial void ServerViolation(this ILogger logger, string reason);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Information, Message = "Session ended: {Code}")]
    public static partial void ClientDisconnected(this ILogger logger, DisconnectCode code);
}
