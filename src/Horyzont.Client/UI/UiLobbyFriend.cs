namespace Horyzont.Client.UI;

/// <summary>A friend who can be joined.</summary>
/// <param name="SteamId">Platform id of the friend.</param>
/// <param name="Name">Display name.</param>
/// <param name="LobbyId">Lobby the friend is in, 0 for none.</param>
public readonly record struct UiLobbyFriend(ulong SteamId, string Name, ulong LobbyId);
