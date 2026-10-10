namespace Horyzont.Client.UI;

/// <summary>The lobby service used without a platform: every request fails with <see cref="UiLobbyError.Unavailable"/>.</summary>
public sealed class UnavailableUiLobbyService : IUiLobbyService
{
    /// <inheritdoc />
    public event Action<ulong>? JoinConfirmationRequested
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event Action<UiLobbyError>? Failed;

    /// <inheritdoc />
    public event Action<UiLobbyState>? StateChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event Action<ulong, bool>? ConnectRequested
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public bool Available => false;

    /// <inheritdoc />
    public UiLobbyState State => UiLobbyState.Unavailable;

    /// <inheritdoc />
    public void Host(UiLobbyVisibility visibility, int capacity) => Failed?.Invoke(UiLobbyError.Unavailable);

    /// <inheritdoc />
    public void Join(ulong lobbyId) => Failed?.Invoke(UiLobbyError.Unavailable);

    /// <inheritdoc />
    public void Invite() => Failed?.Invoke(UiLobbyError.Unavailable);

    /// <inheritdoc />
    public void Leave()
    {
    }

    /// <inheritdoc />
    public int CopyFriends(Span<UiLobbyFriend> into) => 0;

    /// <inheritdoc />
    public void ConfirmJoin(ulong lobbyId)
    {
    }

    /// <inheritdoc />
    public void DeclineJoin()
    {
    }
}
