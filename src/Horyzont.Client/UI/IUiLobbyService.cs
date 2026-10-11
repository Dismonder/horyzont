namespace Horyzont.Client.UI;

/// <summary>What the menu needs from a lobby platform; the Steam adapter is <c>SteamUiLobbyService</c>.</summary>
public interface IUiLobbyService
{
    /// <summary>Raised when an invite asks to leave the current world; answer with <see cref="ConfirmJoin"/> or <see cref="DeclineJoin"/>.</summary>
    event Action<ulong>? JoinConfirmationRequested;

    /// <summary>Raised when an operation failed.</summary>
    event Action<UiLobbyError>? Failed;

    /// <summary>Raised when <see cref="State"/> changed.</summary>
    event Action<UiLobbyState>? StateChanged;

    /// <summary>Raised when a lobby was joined and a session can connect to its owner: the lobby id and whether a password is needed.</summary>
    event Action<ulong, bool>? ConnectRequested;

    /// <summary>Raised after the list of friends was refreshed.</summary>
    event Action? FriendsChanged;

    /// <summary>Gets a value indicating whether the platform can be used.</summary>
    bool Available { get; }

    /// <summary>Gets the state of the service.</summary>
    UiLobbyState State { get; }

    /// <summary>Hosts a lobby.</summary>
    /// <param name="visibility">Who sees it.</param>
    /// <param name="capacity">Maximum players.</param>
    void Host(UiLobbyVisibility visibility, int capacity);

    /// <summary>Joins a lobby.</summary>
    /// <param name="lobbyId">The lobby.</param>
    void Join(ulong lobbyId);

    /// <summary>Opens the invite overlay of the current lobby.</summary>
    void Invite();

    /// <summary>Leaves the current lobby.</summary>
    void Leave();

    /// <summary>Copies the friends that can be joined.</summary>
    /// <param name="into">Destination.</param>
    /// <returns>Number of entries written.</returns>
    int CopyFriends(Span<UiLobbyFriend> into);

    /// <summary>Tells the platform whether the friends list is on screen; showing refreshes it at once and then periodically.</summary>
    /// <param name="visible">Whether the list is visible.</param>
    void SetFriendsVisible(bool visible);

    /// <summary>Accepts a pending invite.</summary>
    /// <param name="lobbyId">The lobby of the invite.</param>
    void ConfirmJoin(ulong lobbyId);

    /// <summary>Declines a pending invite.</summary>
    void DeclineJoin();
}
