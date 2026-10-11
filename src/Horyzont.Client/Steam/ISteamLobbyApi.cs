namespace Horyzont.Client.Steam;

/// <summary>Thin platform surface of Steam lobbies; the Steamworks adapter implements it, tests use a fake. Main thread only.</summary>
public interface ISteamLobbyApi
{
    /// <summary>Raised when a create call finished, with the request id it was started with.</summary>
    event Action<LobbyApiResult>? LobbyCreated;

    /// <summary>Raised when a join call finished, with the request id it was started with.</summary>
    event Action<LobbyApiResult>? LobbyJoined;

    /// <summary>Raised when the user accepts an invite or joins a friend while the game runs.</summary>
    event Action<ulong>? JoinRequested;

    /// <summary>Gets the SteamID of the local user; 0 before a successful initialization.</summary>
    ulong LocalSteamId { get; }

    /// <summary>Gets the persona name of the local user.</summary>
    string DisplayName { get; }

    /// <summary>Initializes the SDK once per process.</summary>
    /// <param name="appId">Steam application id.</param>
    /// <returns>False when Steam is not running or the SDK is missing.</returns>
    bool TryInitialize(uint appId);

    /// <summary>Runs SDK callbacks; once per frame.</summary>
    void Pump();

    /// <summary>Shuts the SDK down; last step of the process.</summary>
    void Shutdown();

    /// <summary>Starts creating a lobby.</summary>
    /// <param name="visibility">Visibility.</param>
    /// <param name="capacity">Member limit.</param>
    /// <param name="requestId">Id returned in <see cref="LobbyCreated"/>.</param>
    void CreateLobby(LobbyVisibility visibility, int capacity, uint requestId);

    /// <summary>Starts joining a lobby.</summary>
    /// <param name="lobbyId">Lobby id.</param>
    /// <param name="requestId">Id returned in <see cref="LobbyJoined"/>.</param>
    void JoinLobby(ulong lobbyId, uint requestId);

    /// <summary>Leaves a lobby; idempotent.</summary>
    /// <param name="lobbyId">Lobby id.</param>
    void LeaveLobby(ulong lobbyId);

    /// <summary>Writes lobby metadata (owner only).</summary>
    /// <param name="lobbyId">Lobby id.</param>
    /// <param name="key">Key.</param>
    /// <param name="value">Value.</param>
    /// <returns>False when the platform refused.</returns>
    bool SetData(ulong lobbyId, string key, string value);

    /// <summary>Reads lobby metadata.</summary>
    /// <param name="lobbyId">Lobby id.</param>
    /// <param name="key">Key.</param>
    /// <returns>The value, empty when absent.</returns>
    string GetData(ulong lobbyId, string key);

    /// <summary>Gets the lobby owner.</summary>
    /// <param name="lobbyId">Lobby id.</param>
    /// <returns>SteamID of the owner; 0 when unknown.</returns>
    ulong GetOwner(ulong lobbyId);

    /// <summary>Gets the member count.</summary>
    /// <param name="lobbyId">Lobby id.</param>
    /// <returns>Number of members.</returns>
    int GetMemberCount(ulong lobbyId);

    /// <summary>Opens the invite overlay of a lobby.</summary>
    /// <param name="lobbyId">Lobby id.</param>
    /// <returns>False when the overlay is unavailable.</returns>
    bool ShowInviteOverlay(ulong lobbyId);

    /// <summary>Copies friends playing this game (same application id) that are in a joinable lobby.</summary>
    /// <param name="destination">Receives at most its length of friends.</param>
    /// <returns>Number of friends written.</returns>
    int CopyJoinableFriends(Span<SteamFriend> destination);
}
