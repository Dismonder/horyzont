using System.Globalization;

using Microsoft.Extensions.Logging;

using Steamworks;

namespace Horyzont.Client.Steam;

/// <summary>Steamworks.NET adapter of <see cref="ISteamLobbyApi"/>; callbacks only translate results and raise events. Main thread only.</summary>
public sealed class SteamworksLobbyApi : ISteamLobbyApi, IDisposable
{
    private readonly ILogger<SteamworksLobbyApi> _logger;
    private readonly List<CallResult<LobbyCreated_t>> _createCalls = [];
    private readonly List<CallResult<LobbyEnter_t>> _joinCalls = [];
    private Callback<GameLobbyJoinRequested_t>? _joinRequested;
    private uint _appId;
    private bool _initialized;
    private bool _shutdown;

    /// <summary>Initializes a new instance of the <see cref="SteamworksLobbyApi"/> class.</summary>
    /// <param name="logger">Logger.</param>
    public SteamworksLobbyApi(ILogger<SteamworksLobbyApi> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public event Action<LobbyApiResult>? LobbyCreated;

    /// <inheritdoc />
    public event Action<LobbyApiResult>? LobbyJoined;

    /// <inheritdoc />
    public event Action<ulong>? JoinRequested;

    /// <inheritdoc />
    public ulong LocalSteamId { get; private set; }

    /// <inheritdoc />
    public string DisplayName { get; private set; } = "";

    /// <inheritdoc />
    public bool TryInitialize(uint appId)
    {
        if (_initialized)
        {
            return true;
        }

        // The SDK reads the application id from these variables when no steam_appid.txt sits next to the executable.
        var text = appId.ToString(CultureInfo.InvariantCulture);
        Environment.SetEnvironmentVariable("SteamAppId", text);
        Environment.SetEnvironmentVariable("SteamGameId", text);
        try
        {
            if (!SteamAPI.Init())
            {
                _logger.InitFailed("SteamAPI.Init returned false (is Steam running?)");
                return false;
            }
        }
#pragma warning disable CA1031 // Native SDK problems (missing library, wrong architecture) must degrade to "Steam unavailable", logged here.
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
#pragma warning restore CA1031
        {
            _logger.InitFailed(ex.Message);
            return false;
        }

        _appId = appId;
        _initialized = true;
        LocalSteamId = SteamUser.GetSteamID().m_SteamID;
        DisplayName = SteamFriends.GetPersonaName();
        _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
        return true;
    }

    /// <inheritdoc />
    public void Pump()
    {
        if (_initialized && !_shutdown)
        {
            SteamAPI.RunCallbacks();
        }
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        if (!_initialized || _shutdown)
        {
            return;
        }

        _shutdown = true;
        Dispose();
        SteamAPI.Shutdown();
    }

    /// <inheritdoc />
    public void CreateLobby(LobbyVisibility visibility, int capacity, uint requestId)
    {
        if (!Ready())
        {
            LobbyCreated?.Invoke(new LobbyApiResult(requestId, 0, LobbyError.Unavailable));
            return;
        }

        var call = CallResult<LobbyCreated_t>.Create();
        _createCalls.Add(call);
        call.Set(SteamMatchmaking.CreateLobby(ToLobbyType(visibility), capacity), (result, ioFailure) =>
        {
            _createCalls.Remove(call);
            call.Dispose();
            LobbyCreated?.Invoke(new LobbyApiResult(requestId, result.m_ulSteamIDLobby, MapCreateResult(result.m_eResult, ioFailure)));
        });
    }

    /// <inheritdoc />
    public void JoinLobby(ulong lobbyId, uint requestId)
    {
        if (!Ready())
        {
            LobbyJoined?.Invoke(new LobbyApiResult(requestId, 0, LobbyError.Unavailable));
            return;
        }

        var call = CallResult<LobbyEnter_t>.Create();
        _joinCalls.Add(call);
        call.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobbyId)), (result, ioFailure) =>
        {
            _joinCalls.Remove(call);
            call.Dispose();
            LobbyJoined?.Invoke(new LobbyApiResult(requestId, result.m_ulSteamIDLobby, MapEnterResponse(result.m_EChatRoomEnterResponse, ioFailure)));
        });
    }

    /// <inheritdoc />
    public void LeaveLobby(ulong lobbyId)
    {
        if (Ready() && lobbyId != 0)
        {
            SteamMatchmaking.LeaveLobby(new CSteamID(lobbyId));
        }
    }

    /// <inheritdoc />
    public bool SetData(ulong lobbyId, string key, string value) => Ready() && SteamMatchmaking.SetLobbyData(new CSteamID(lobbyId), key, value);

    /// <inheritdoc />
    public string GetData(ulong lobbyId, string key) => Ready() ? SteamMatchmaking.GetLobbyData(new CSteamID(lobbyId), key) : "";

    /// <inheritdoc />
    public ulong GetOwner(ulong lobbyId) => Ready() ? SteamMatchmaking.GetLobbyOwner(new CSteamID(lobbyId)).m_SteamID : 0;

    /// <inheritdoc />
    public int GetMemberCount(ulong lobbyId) => Ready() ? SteamMatchmaking.GetNumLobbyMembers(new CSteamID(lobbyId)) : 0;

    /// <inheritdoc />
    public bool ShowInviteOverlay(ulong lobbyId)
    {
        if (!Ready())
        {
            return false;
        }

        SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(lobbyId));
        return true;
    }

    /// <inheritdoc />
    public int CopyJoinableFriends(Span<SteamFriend> destination)
    {
        if (!Ready())
        {
            return 0;
        }

        var written = 0;
        var count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
        for (var i = 0; i < count && written < destination.Length; i++)
        {
            var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
            if (!SteamFriends.GetFriendGamePlayed(id, out var game) || !IsJoinable(game.m_gameID.AppID().m_AppId, game.m_steamIDLobby.m_SteamID, _appId))
            {
                continue;
            }

            destination[written++] = new SteamFriend(id.m_SteamID, SteamFriends.GetFriendPersonaName(id), game.m_steamIDLobby.m_SteamID);
        }

        return written;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _joinRequested?.Dispose();
        _joinRequested = null;
        foreach (var call in _createCalls)
        {
            call.Dispose();
        }

        foreach (var call in _joinCalls)
        {
            call.Dispose();
        }

        _createCalls.Clear();
        _joinCalls.Clear();
    }

    internal static ELobbyType ToLobbyType(LobbyVisibility visibility) => visibility switch
    {
        LobbyVisibility.Private => ELobbyType.k_ELobbyTypePrivate,
        LobbyVisibility.FriendsOnly => ELobbyType.k_ELobbyTypeFriendsOnly,
        LobbyVisibility.Public => ELobbyType.k_ELobbyTypePublic,
        _ => throw new ArgumentOutOfRangeException(nameof(visibility)),
    };

    internal static LobbyError MapCreateResult(EResult result, bool ioFailure)
    {
        if (ioFailure)
        {
            return LobbyError.PlatformFailure;
        }

        return result switch
        {
            EResult.k_EResultOK => LobbyError.None,
            EResult.k_EResultTimeout => LobbyError.Timeout,
            EResult.k_EResultAccessDenied => LobbyError.AccessDenied,
            _ => LobbyError.PlatformFailure,
        };
    }

    internal static LobbyError MapEnterResponse(uint response, bool ioFailure)
    {
        if (ioFailure)
        {
            return LobbyError.PlatformFailure;
        }

        return (EChatRoomEnterResponse)response switch
        {
            EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess => LobbyError.None,
            EChatRoomEnterResponse.k_EChatRoomEnterResponseFull => LobbyError.Full,
            EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist => LobbyError.Unavailable,
            EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed
                or EChatRoomEnterResponse.k_EChatRoomEnterResponseBanned
                or EChatRoomEnterResponse.k_EChatRoomEnterResponseLimited
                or EChatRoomEnterResponse.k_EChatRoomEnterResponseClanDisabled
                or EChatRoomEnterResponse.k_EChatRoomEnterResponseCommunityBan
                or EChatRoomEnterResponse.k_EChatRoomEnterResponseMemberBlockedYou
                or EChatRoomEnterResponse.k_EChatRoomEnterResponseYouBlockedMember => LobbyError.AccessDenied,
            _ => LobbyError.PlatformFailure,
        };
    }

    internal static bool IsJoinable(uint friendAppId, ulong lobbyId, uint appId) => friendAppId == appId && lobbyId != 0;

    private bool Ready() => _initialized && !_shutdown;

    private void OnJoinRequested(GameLobbyJoinRequested_t request) => JoinRequested?.Invoke(request.m_steamIDLobby.m_SteamID);
}
