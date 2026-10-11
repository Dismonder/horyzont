namespace Horyzont.Client.Steam;

/// <summary>Who can find and join a lobby.</summary>
public enum LobbyVisibility
{
    /// <summary>Only invited players can join.</summary>
    Private,

    /// <summary>Friends of the host can join.</summary>
    FriendsOnly,

    /// <summary>Anyone with the lobby id can join.</summary>
    Public,
}
