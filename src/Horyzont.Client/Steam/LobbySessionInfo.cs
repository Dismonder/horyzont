namespace Horyzont.Client.Steam;

/// <summary>Public facts of the hosted world written to lobby metadata; never the password.</summary>
/// <param name="WorldId">World id in "D" format.</param>
/// <param name="PasswordRequired">Whether the server requires a password.</param>
public readonly record struct LobbySessionInfo(string WorldId, bool PasswordRequired);
