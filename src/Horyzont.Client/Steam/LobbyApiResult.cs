namespace Horyzont.Client.Steam;

/// <summary>Result of an asynchronous lobby API call.</summary>
/// <param name="RequestId">Id given to the call that produced this result (also for a late callback).</param>
/// <param name="LobbyId">Lobby id; 0 when none.</param>
/// <param name="Error">Translated result.</param>
public readonly record struct LobbyApiResult(uint RequestId, ulong LobbyId, LobbyError Error);
