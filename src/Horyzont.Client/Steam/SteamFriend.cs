namespace Horyzont.Client.Steam;

/// <summary>A friend who plays this game and sits in a joinable lobby.</summary>
/// <param name="SteamId">Friend SteamID.</param>
/// <param name="Name">Persona name; plain text.</param>
/// <param name="LobbyId">Lobby the friend is in.</param>
public readonly record struct SteamFriend(ulong SteamId, string Name, ulong LobbyId);
