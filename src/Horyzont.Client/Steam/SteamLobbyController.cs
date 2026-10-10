using Horyzont.Net.Transport;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Steam;

/// <summary>Lobby state machine on top of <see cref="ISteamLobbyApi"/>. Main thread only.</summary>
public sealed class SteamLobbyController : IDisposable
{
    /// <summary>Initializes a new instance of the <see cref="SteamLobbyController"/> class.</summary>
    /// <param name="api">Platform surface.</param>
    /// <param name="time">Clock of deadlines and polling.</param>
    /// <param name="isSessionActive">Whether a game session is running (an invite then needs a confirmation).</param>
    /// <param name="logger">Logger.</param>
    public SteamLobbyController(ISteamLobbyApi api, TimeProvider time, Func<bool> isSessionActive, ILogger<SteamLobbyController> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(isSessionActive);
        ArgumentNullException.ThrowIfNull(logger);
        throw new NotImplementedException();
    }

    /// <summary>Raised with the host endpoint (never the lobby id) and whether a password is required, once a lobby passed every check.</summary>
    public event Action<SteamEndpoint, bool>? ConnectRequested;

    /// <summary>Raised when an operation failed.</summary>
    public event Action<LobbyError>? Failed;

    /// <summary>Raised when an invite arrived during a running session; answer with <see cref="ConfirmJoin"/> or <see cref="DeclineJoin"/>.</summary>
    public event Action<ulong>? JoinConfirmationRequested;

    /// <summary>Raised after every state change.</summary>
    public event Action<LobbyState>? StateChanged;

    /// <summary>Raised after the friend list was refreshed.</summary>
    public event Action? FriendsChanged;

    /// <summary>Gets the state.</summary>
    public LobbyState State => throw new NotImplementedException();

    /// <summary>Gets the active lobby, if any.</summary>
    public LobbyInfo? Current => throw new NotImplementedException();

    /// <summary>Gets the joinable friends of the last refresh.</summary>
    public IReadOnlyList<SteamFriend> Friends => throw new NotImplementedException();

    /// <summary>Sets the local build hash and protocol version that lobbies are published with and checked against.</summary>
    /// <param name="buildHash">Build hash as decimal text.</param>
    /// <param name="protocolVersion">Protocol version.</param>
    public void Configure(string buildHash, ushort protocolVersion) => throw new NotImplementedException();

    /// <summary>Marks Steam as unavailable in this process.</summary>
    public void SetUnavailable() => throw new NotImplementedException();

    /// <summary>Sets the world facts published by the next <see cref="Host"/>.</summary>
    /// <param name="session">World facts.</param>
    public void SetSession(LobbySessionInfo session) => throw new NotImplementedException();

    /// <summary>Creates a lobby.</summary>
    /// <param name="visibility">Visibility.</param>
    /// <param name="capacity">Member limit.</param>
    public void Host(LobbyVisibility visibility, int capacity) => throw new NotImplementedException();

    /// <summary>Joins a lobby and checks it.</summary>
    /// <param name="lobbyId">Lobby id.</param>
    public void Join(ulong lobbyId) => throw new NotImplementedException();

    /// <summary>Opens the invite overlay of the active lobby.</summary>
    public void Invite() => throw new NotImplementedException();

    /// <summary>Leaves the lobby or cancels a pending operation; idempotent.</summary>
    public void Leave() => throw new NotImplementedException();

    /// <summary>Marks a hosted lobby not ready and cancels a pending operation; the lobby itself is left later by <see cref="Leave"/>.</summary>
    public void PrepareShutdown() => throw new NotImplementedException();

    /// <summary>Accepts the pending join request; the join starts when the running session has ended.</summary>
    /// <param name="lobbyId">Lobby id from <see cref="JoinConfirmationRequested"/>.</param>
    public void ConfirmJoin(ulong lobbyId) => throw new NotImplementedException();

    /// <summary>Drops the pending join request.</summary>
    public void DeclineJoin() => throw new NotImplementedException();

    /// <summary>Shows or hides the friend panel; showing refreshes at once, then every 5 seconds while visible.</summary>
    /// <param name="visible">Whether the panel is visible.</param>
    public void SetFriendsVisible(bool visible) => throw new NotImplementedException();

    /// <summary>Advances deadlines, owner polling, friend refresh and a confirmed join; once per frame.</summary>
    public void Update() => throw new NotImplementedException();

    /// <inheritdoc />
    public void Dispose() => throw new NotImplementedException();

    private void Unused()
    {
        ConnectRequested?.Invoke(new SteamEndpoint(0), false);
        Failed?.Invoke(LobbyError.None);
        JoinConfirmationRequested?.Invoke(0);
        StateChanged?.Invoke(LobbyState.Idle);
        FriendsChanged?.Invoke();
    }
}
