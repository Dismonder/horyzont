using Horyzont.Client.UI;

namespace Horyzont.Client.Steam;

/// <summary>
/// Adapts the process-wide <see cref="SteamLobbyController"/> (K3-17) to the lobby surface of the menu (K3-18). It owns no state: every
/// call is forwarded and every controller notification is mapped one to one. The endpoint of a joined lobby is never shown to the menu;
/// the session that follows reads it again from the controller through its own platform binder.
/// </summary>
public sealed class SteamUiLobbyService : IUiLobbyService, IDisposable
{
    private readonly SteamLobbyController _lobby;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="SteamUiLobbyService"/> class.</summary>
    /// <param name="lobby">The process lobby controller.</param>
    public SteamUiLobbyService(SteamLobbyController lobby)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        _lobby = lobby;
        _lobby.ConnectRequested += OnConnectRequested;
        _lobby.Failed += OnFailed;
        _lobby.JoinConfirmationRequested += OnJoinConfirmationRequested;
        _lobby.StateChanged += OnStateChanged;
        _lobby.FriendsChanged += OnFriendsChanged;
    }

    /// <inheritdoc />
    public event Action<ulong>? JoinConfirmationRequested;

    /// <inheritdoc />
    public event Action<UiLobbyError>? Failed;

    /// <inheritdoc />
    public event Action<UiLobbyState>? StateChanged;

    /// <inheritdoc />
    public event Action<ulong, bool>? ConnectRequested;

    /// <inheritdoc />
    public event Action? FriendsChanged;

    /// <inheritdoc />
    public bool Available => _lobby.State != LobbyState.Unavailable;

    /// <inheritdoc />
    public UiLobbyState State => MapState(_lobby.State);

    /// <summary>Maps a controller error to the menu error.</summary>
    /// <param name="error">Controller error.</param>
    /// <returns>Menu error.</returns>
    public static UiLobbyError MapError(LobbyError error) => error switch
    {
        LobbyError.None => UiLobbyError.None,
        LobbyError.Unavailable => UiLobbyError.Unavailable,
        LobbyError.Timeout => UiLobbyError.Timeout,
        LobbyError.Full => UiLobbyError.Full,
        LobbyError.AccessDenied => UiLobbyError.AccessDenied,
        LobbyError.InvalidMetadata => UiLobbyError.InvalidMetadata,
        LobbyError.IncompatibleBuild => UiLobbyError.IncompatibleBuild,
        LobbyError.OwnerChanged => UiLobbyError.OwnerChanged,
        LobbyError.Cancelled => UiLobbyError.Cancelled,
        _ => UiLobbyError.PlatformFailure,
    };

    /// <summary>Maps a controller state to the menu state.</summary>
    /// <param name="state">Controller state.</param>
    /// <returns>Menu state.</returns>
    public static UiLobbyState MapState(LobbyState state) => state switch
    {
        LobbyState.Unavailable => UiLobbyState.Unavailable,
        LobbyState.Idle => UiLobbyState.Idle,
        LobbyState.Creating => UiLobbyState.Creating,
        LobbyState.Hosting => UiLobbyState.Hosting,
        LobbyState.Joining => UiLobbyState.Joining,
        LobbyState.Joined => UiLobbyState.Joined,
        LobbyState.Leaving => UiLobbyState.Leaving,
        _ => UiLobbyState.Failed,
    };

    /// <summary>Maps the visibility chosen in the menu to the controller visibility.</summary>
    /// <param name="visibility">Menu visibility.</param>
    /// <returns>Controller visibility.</returns>
    public static LobbyVisibility MapVisibility(UiLobbyVisibility visibility) => visibility switch
    {
        UiLobbyVisibility.Private => LobbyVisibility.Private,
        UiLobbyVisibility.Public => LobbyVisibility.Public,
        _ => LobbyVisibility.FriendsOnly,
    };

    /// <inheritdoc />
    public void Host(UiLobbyVisibility visibility, int capacity) => _lobby.Host(MapVisibility(visibility), capacity);

    /// <inheritdoc />
    public void Join(ulong lobbyId) => _lobby.Join(lobbyId);

    /// <inheritdoc />
    public void Invite() => _lobby.Invite();

    /// <inheritdoc />
    public void Leave() => _lobby.Leave();

    /// <inheritdoc />
    public int CopyFriends(Span<UiLobbyFriend> into)
    {
        var friends = _lobby.Friends;
        var count = Math.Min(friends.Count, into.Length);
        for (var i = 0; i < count; i++)
        {
            var friend = friends[i];
            into[i] = new UiLobbyFriend(friend.SteamId, friend.Name, friend.LobbyId);
        }

        return count;
    }

    /// <inheritdoc />
    public void SetFriendsVisible(bool visible) => _lobby.SetFriendsVisible(visible);

    /// <inheritdoc />
    public void ConfirmJoin(ulong lobbyId) => _lobby.ConfirmJoin(lobbyId);

    /// <inheritdoc />
    public void DeclineJoin() => _lobby.DeclineJoin();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lobby.ConnectRequested -= OnConnectRequested;
        _lobby.Failed -= OnFailed;
        _lobby.JoinConfirmationRequested -= OnJoinConfirmationRequested;
        _lobby.StateChanged -= OnStateChanged;
        _lobby.FriendsChanged -= OnFriendsChanged;
    }

    private void OnConnectRequested(Net.Transport.SteamEndpoint endpoint, bool passwordRequired) =>
        ConnectRequested?.Invoke(_lobby.Current?.LobbyId ?? 0, passwordRequired);

    private void OnFailed(LobbyError error) => Failed?.Invoke(MapError(error));

    private void OnJoinConfirmationRequested(ulong lobbyId) => JoinConfirmationRequested?.Invoke(lobbyId);

    private void OnStateChanged(LobbyState state) => StateChanged?.Invoke(MapState(state));

    private void OnFriendsChanged() => FriendsChanged?.Invoke();
}
