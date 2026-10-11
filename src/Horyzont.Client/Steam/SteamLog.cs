using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Steam;

/// <summary>Log entries of the Steam lobby code. Never logs passwords or auth tickets.</summary>
internal static partial class SteamLog
{
    [LoggerMessage(EventId = 9101, Level = LogLevel.Warning, Message = "Steam is unavailable: {Reason}")]
    public static partial void InitFailed(this ILogger logger, string reason);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Information, Message = "Steam initialized (app {AppId}, user {SteamId})")]
    public static partial void Initialized(this ILogger logger, uint appId, ulong steamId);

    [LoggerMessage(EventId = 9103, Level = LogLevel.Information, Message = "Lobby {LobbyId}: {Operation} started")]
    public static partial void OperationStarted(this ILogger logger, ulong lobbyId, string operation);

    [LoggerMessage(EventId = 9104, Level = LogLevel.Warning, Message = "Lobby {LobbyId}: {Operation} failed with {Error}")]
    public static partial void OperationFailed(this ILogger logger, ulong lobbyId, string operation, LobbyError error);

    [LoggerMessage(EventId = 9105, Level = LogLevel.Information, Message = "Lobby {LobbyId} is {State}")]
    public static partial void LobbyStateChanged(this ILogger logger, ulong lobbyId, LobbyState state);

    [LoggerMessage(EventId = 9106, Level = LogLevel.Information, Message = "Join request for lobby {LobbyId} ignored: {Reason}")]
    public static partial void JoinIgnored(this ILogger logger, ulong lobbyId, string reason);

    [LoggerMessage(EventId = 9107, Level = LogLevel.Warning, Message = "Stale result for lobby {LobbyId} (request {RequestId}); leaving it")]
    public static partial void StaleResult(this ILogger logger, ulong lobbyId, uint requestId);

    [LoggerMessage(EventId = 9108, Level = LogLevel.Information, Message = "Steam P2P host {HostSteamId} for lobby {LobbyId}")]
    public static partial void ConnectRequested(this ILogger logger, ulong hostSteamId, ulong lobbyId);

    [LoggerMessage(EventId = 9109, Level = LogLevel.Information, Message = "Steam services shut down")]
    public static partial void ServicesShutDown(this ILogger logger);

    [LoggerMessage(EventId = 9110, Level = LogLevel.Warning, Message = "Lobby of host {HostSteamId} requires a password and none was given; the server will refuse")]
    public static partial void PasswordMissing(this ILogger logger, ulong hostSteamId);

    [LoggerMessage(EventId = 9111, Level = LogLevel.Warning, Message = "Ending the client session: {Error}")]
    public static partial void ClientSessionEnded(this ILogger logger, LobbyError error);

    [LoggerMessage(EventId = 9112, Level = LogLevel.Information, Message = "Lobby {LobbyId}: {Operation} ignored while {State}")]
    public static partial void OperationIgnored(this ILogger logger, ulong lobbyId, string operation, LobbyState state);
}
