using System.Globalization;

using Horyzont.Net.Transport;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Steam;

/// <summary>
/// Lobby state machine on top of <see cref="ISteamLobbyApi"/>. Main thread only. Lobby metadata is informational: it never replaces
/// the server handshake, and the endpoint always comes from the lobby owner, never from the lobby id.
/// </summary>
public sealed class SteamLobbyController : IDisposable
{
    private const int MaxFriends = 256;
    private const int MaxCapacity = 256;

    private static readonly TimeSpan _operationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _ownerPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _friendRefreshInterval = TimeSpan.FromSeconds(5);

    private readonly ISteamLobbyApi _api;
    private readonly TimeProvider _time;
    private readonly Func<bool> _isSessionActive;
    private readonly ILogger<SteamLobbyController> _logger;
    private readonly SteamFriend[] _friendBuffer = new SteamFriend[MaxFriends];
    private LobbyState _state = LobbyState.Idle;
    private LobbyInfo? _current;
    private LobbySessionInfo? _session;
    private string _buildHash = "";
    private ushort _protocolVersion;
    private bool _configured;
    private uint _generation;
    private long _operationStart;
    private long _lastOwnerPoll;
    private long _lastFriendRefresh;
    private LobbyVisibility _creatingVisibility;
    private int _creatingCapacity;
    private bool _notReady;
    private bool _friendsVisible;
    private ulong _pendingConfirmation;
    private ulong _confirmedJoin;
    private IReadOnlyList<SteamFriend> _friends = [];
    private bool _disposed;

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
        _api = api;
        _time = time;
        _isSessionActive = isSessionActive;
        _logger = logger;
        _api.LobbyCreated += OnLobbyCreated;
        _api.LobbyJoined += OnLobbyJoined;
        _api.JoinRequested += OnJoinRequested;
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
    public LobbyState State => _state;

    /// <summary>Gets the active lobby, if any.</summary>
    public LobbyInfo? Current => _current;

    /// <summary>Gets the joinable friends of the last refresh.</summary>
    public IReadOnlyList<SteamFriend> Friends => _friends;

    /// <summary>Sets the local build hash and protocol version that lobbies are published with and checked against.</summary>
    /// <param name="buildHash">Build hash as decimal text.</param>
    /// <param name="protocolVersion">Protocol version.</param>
    public void Configure(string buildHash, ushort protocolVersion)
    {
        ArgumentException.ThrowIfNullOrEmpty(buildHash);
        _buildHash = buildHash;
        _protocolVersion = protocolVersion;
        _configured = true;
    }

    /// <summary>Marks Steam as unavailable in this process.</summary>
    public void SetUnavailable() => SetState(LobbyState.Unavailable);

    /// <summary>Sets the world facts published by the next <see cref="Host"/>.</summary>
    /// <param name="session">World facts.</param>
    public void SetSession(LobbySessionInfo session) => _session = session;

    /// <summary>Creates a lobby.</summary>
    /// <param name="visibility">Visibility.</param>
    /// <param name="capacity">Member limit.</param>
    public void Host(LobbyVisibility visibility, int capacity)
    {
        if (!CanStart(0, "create"))
        {
            return;
        }

        if (_session is null || !_configured || capacity is < 1 or > MaxCapacity)
        {
            Fail(LobbyError.InvalidMetadata, 0, "create");
            return;
        }

        _creatingVisibility = visibility;
        _creatingCapacity = capacity;
        BeginOperation(LobbyState.Creating);
        _logger.OperationStarted(0, "create");
        _api.CreateLobby(visibility, capacity, _generation);
    }

    /// <summary>Joins a lobby and checks it.</summary>
    /// <param name="lobbyId">Lobby id.</param>
    public void Join(ulong lobbyId)
    {
        if (!CanStart(lobbyId, "join"))
        {
            return;
        }

        if (lobbyId == 0)
        {
            Fail(LobbyError.InvalidMetadata, lobbyId, "join");
            return;
        }

        if (!_configured)
        {
            Fail(LobbyError.IncompatibleBuild, lobbyId, "join");
            return;
        }

        BeginOperation(LobbyState.Joining);
        _logger.OperationStarted(lobbyId, "join");
        _api.JoinLobby(lobbyId, _generation);
    }

    /// <summary>Opens the invite overlay of the active lobby.</summary>
    public void Invite()
    {
        if (_state is LobbyState.Hosting or LobbyState.Joined && _current is { } lobby)
        {
            _api.ShowInviteOverlay(lobby.LobbyId);
        }
    }

    /// <summary>Leaves the lobby or cancels a pending operation; idempotent.</summary>
    public void Leave()
    {
        switch (_state)
        {
            case LobbyState.Creating:
            case LobbyState.Joining:
                _generation++;
                SetState(LobbyState.Idle);
                break;
            case LobbyState.Hosting:
            case LobbyState.Joined:
                LeaveCurrent();
                break;
            case LobbyState.Failed:
                SetState(LobbyState.Idle);
                break;
        }
    }

    /// <summary>Marks a hosted lobby not ready and cancels a pending operation; the lobby itself is left later by <see cref="Leave"/>.</summary>
    public void PrepareShutdown()
    {
        _pendingConfirmation = 0;
        _confirmedJoin = 0;
        if (_state is LobbyState.Creating or LobbyState.Joining)
        {
            Leave();
        }
        else if (_state == LobbyState.Hosting && !_notReady && _current is { } lobby)
        {
            _notReady = true;
            _api.SetData(lobby.LobbyId, LobbyMetadata.Ready, "0");
        }
    }

    /// <summary>Accepts the pending join request; the join starts when the running session has ended.</summary>
    /// <param name="lobbyId">Lobby id from <see cref="JoinConfirmationRequested"/>.</param>
    public void ConfirmJoin(ulong lobbyId)
    {
        if (lobbyId != 0 && lobbyId == _pendingConfirmation)
        {
            _pendingConfirmation = 0;
            _confirmedJoin = lobbyId;
        }
    }

    /// <summary>Drops the pending join request.</summary>
    public void DeclineJoin()
    {
        _pendingConfirmation = 0;
        _confirmedJoin = 0;
    }

    /// <summary>Shows or hides the friend panel; showing refreshes at once, then every 5 seconds while visible.</summary>
    /// <param name="visible">Whether the panel is visible.</param>
    public void SetFriendsVisible(bool visible)
    {
        _friendsVisible = visible;
        if (visible)
        {
            RefreshFriends();
        }
    }

    /// <summary>Advances deadlines, owner polling, friend refresh and a confirmed join; once per frame.</summary>
    public void Update()
    {
        if (_disposed)
        {
            return;
        }

        var now = _time.GetTimestamp();
        if (_state is LobbyState.Creating or LobbyState.Joining && _time.GetElapsedTime(_operationStart, now) >= _operationTimeout)
        {
            var operation = _state == LobbyState.Creating ? "create" : "join";
            _generation++;
            Fail(LobbyError.Timeout, 0, operation);
        }

        if (_state is LobbyState.Hosting or LobbyState.Joined && _time.GetElapsedTime(_lastOwnerPoll, now) >= _ownerPollInterval)
        {
            PollOwner(now);
        }

        if (_friendsVisible && _time.GetElapsedTime(_lastFriendRefresh, now) >= _friendRefreshInterval)
        {
            RefreshFriends();
        }

        if (_confirmedJoin != 0 && _state != LobbyState.Unavailable && !_isSessionActive())
        {
            var lobbyId = _confirmedJoin;
            _confirmedJoin = 0;
            Leave();
            Join(lobbyId);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _api.LobbyCreated -= OnLobbyCreated;
        _api.LobbyJoined -= OnLobbyJoined;
        _api.JoinRequested -= OnJoinRequested;
    }

    private bool CanStart(ulong lobbyId, string operation)
    {
        if (_state == LobbyState.Unavailable)
        {
            Failed?.Invoke(LobbyError.Unavailable);
            return false;
        }

        if (_state is not (LobbyState.Idle or LobbyState.Failed))
        {
            _logger.OperationIgnored(lobbyId, operation, _state);
            return false;
        }

        return true;
    }

    private void BeginOperation(LobbyState state)
    {
        _generation++;
        _operationStart = _time.GetTimestamp();
        SetState(state);
    }

    private void SetState(LobbyState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        _logger.LobbyStateChanged(_current?.LobbyId ?? 0, state);
        StateChanged?.Invoke(state);
    }

    private void Fail(LobbyError error, ulong lobbyId, string operation)
    {
        _logger.OperationFailed(lobbyId, operation, error);
        _current = null;
        _notReady = false;
        SetState(LobbyState.Failed);
        Failed?.Invoke(error);
    }

    private void OnLobbyCreated(LobbyApiResult result)
    {
        if (result.RequestId != _generation || _state != LobbyState.Creating)
        {
            DropStale(result);
            return;
        }

        if (result.Error != LobbyError.None || result.LobbyId == 0)
        {
            Fail(result.Error != LobbyError.None ? result.Error : LobbyError.PlatformFailure, result.LobbyId, "create");
            return;
        }

        var lobby = result.LobbyId;
        var host = _api.LocalSteamId;
        var session = _session!.Value;
        var capacity = _creatingCapacity.ToString(CultureInfo.InvariantCulture);
        (string Key, string Value)[] metadata =
        [
            (LobbyMetadata.Schema, LobbyMetadata.SchemaValue),
            (LobbyMetadata.Host, host.ToString(CultureInfo.InvariantCulture)),
            (LobbyMetadata.Build, _buildHash),
            (LobbyMetadata.Protocol, _protocolVersion.ToString(CultureInfo.InvariantCulture)),
            (LobbyMetadata.World, session.WorldId),
            (LobbyMetadata.Capacity, capacity),
            (LobbyMetadata.Password, session.PasswordRequired ? "1" : "0"),
            (LobbyMetadata.Ready, "1"),
        ];
        foreach (var (key, value) in metadata)
        {
            if (value.Length > LobbyMetadata.MaxValueLength || !_api.SetData(lobby, key, value))
            {
                _api.LeaveLobby(lobby);
                Fail(LobbyError.PlatformFailure, lobby, "create");
                return;
            }
        }

        _notReady = false;
        _current = new LobbyInfo(
            lobby, host, _creatingVisibility, Math.Max(1, _api.GetMemberCount(lobby)), _creatingCapacity, _buildHash, _protocolVersion);
        _lastOwnerPoll = _time.GetTimestamp();
        SetState(LobbyState.Hosting);
    }

    private void OnLobbyJoined(LobbyApiResult result)
    {
        if (result.RequestId != _generation || _state != LobbyState.Joining)
        {
            DropStale(result);
            return;
        }

        if (result.Error != LobbyError.None || result.LobbyId == 0)
        {
            Fail(result.Error != LobbyError.None ? result.Error : LobbyError.PlatformFailure, result.LobbyId, "join");
            return;
        }

        var lobby = result.LobbyId;
        var error = Inspect(lobby, out var info, out var passwordRequired);
        if (error != LobbyError.None)
        {
            _api.LeaveLobby(lobby);
            Fail(error, lobby, "join");
            return;
        }

        _current = info;
        _lastOwnerPoll = _time.GetTimestamp();
        SetState(LobbyState.Joined);
        _logger.ConnectRequested(info.HostSteamId, lobby);
        ConnectRequested?.Invoke(new SteamEndpoint(info.HostSteamId), passwordRequired);
    }

    private void DropStale(LobbyApiResult result)
    {
        // A successful result of a cancelled or timed-out call: the lobby was entered anyway, so leave it, unless it is the live one.
        if (result.Error == LobbyError.None && result.LobbyId != 0 && result.LobbyId != _current?.LobbyId)
        {
            _logger.StaleResult(result.LobbyId, result.RequestId);
            _api.LeaveLobby(result.LobbyId);
        }
    }

    private LobbyError Inspect(ulong lobby, out LobbyInfo info, out bool passwordRequired)
    {
        info = default;
        passwordRequired = false;
        var schema = _api.GetData(lobby, LobbyMetadata.Schema);
        var hostText = _api.GetData(lobby, LobbyMetadata.Host);
        var build = _api.GetData(lobby, LobbyMetadata.Build);
        var protocolText = _api.GetData(lobby, LobbyMetadata.Protocol);
        var world = _api.GetData(lobby, LobbyMetadata.World);
        var capacityText = _api.GetData(lobby, LobbyMetadata.Capacity);
        var password = _api.GetData(lobby, LobbyMetadata.Password);
        var ready = _api.GetData(lobby, LobbyMetadata.Ready);
        string[] all = [schema, hostText, build, protocolText, world, capacityText, password, ready];
        if (all.Any(v => v.Length == 0 || v.Length > LobbyMetadata.MaxValueLength)
            || schema != LobbyMetadata.SchemaValue
            || ready != "1"
            || password is not ("0" or "1")
            || !Guid.TryParseExact(world, "D", out _)
            || !ulong.TryParse(hostText, NumberStyles.None, CultureInfo.InvariantCulture, out var host) || host == 0
            || !ulong.TryParse(build, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            || !ushort.TryParse(protocolText, NumberStyles.None, CultureInfo.InvariantCulture, out var protocol)
            || !int.TryParse(capacityText, NumberStyles.None, CultureInfo.InvariantCulture, out var capacity) || capacity is < 1 or > MaxCapacity
            || _api.GetOwner(lobby) != host)
        {
            return LobbyError.InvalidMetadata;
        }

        if (build != _buildHash || protocol != _protocolVersion)
        {
            return LobbyError.IncompatibleBuild;
        }

        passwordRequired = password == "1";
        info = new LobbyInfo(lobby, host, LobbyVisibility.Private, Math.Max(1, _api.GetMemberCount(lobby)), capacity, build, protocol);
        return LobbyError.None;
    }

    private void OnJoinRequested(ulong lobbyId)
    {
        if (lobbyId == 0)
        {
            return;
        }

        if (!_isSessionActive())
        {
            Join(lobbyId);
            return;
        }

        if (JoinConfirmationRequested is null)
        {
            _logger.JoinIgnored(lobbyId, "a session is running and nothing can ask the player");
            return;
        }

        _pendingConfirmation = lobbyId;
        JoinConfirmationRequested.Invoke(lobbyId);
    }

    private void PollOwner(long now)
    {
        _lastOwnerPoll = now;
        var lobby = _current!.Value;
        var owner = _api.GetOwner(lobby.LobbyId);
        if (owner != lobby.HostSteamId)
        {
            LeaveCurrent();
            Fail(LobbyError.OwnerChanged, lobby.LobbyId, "owner");
            return;
        }

        _current = lobby with { Members = Math.Max(1, _api.GetMemberCount(lobby.LobbyId)) };
    }

    private void LeaveCurrent()
    {
        var lobby = _current!.Value;
        var hosting = _state == LobbyState.Hosting;
        SetState(LobbyState.Leaving);
        if (hosting && !_notReady)
        {
            _api.SetData(lobby.LobbyId, LobbyMetadata.Ready, "0");
        }

        _api.LeaveLobby(lobby.LobbyId);
        _current = null;
        _notReady = false;
        SetState(LobbyState.Idle);
    }

    private void RefreshFriends()
    {
        var count = _api.CopyJoinableFriends(_friendBuffer);
        var list = new List<SteamFriend>(count);
        for (var i = 0; i < count && i < _friendBuffer.Length; i++)
        {
            if (_friendBuffer[i].LobbyId != 0)
            {
                list.Add(_friendBuffer[i]);
            }
        }

        list.Sort(static (a, b) =>
        {
            var byName = string.CompareOrdinal(a.Name, b.Name);
            return byName != 0 ? byName : a.SteamId.CompareTo(b.SteamId);
        });
        if (list.Count > MaxFriends)
        {
            list.RemoveRange(MaxFriends, list.Count - MaxFriends);
        }

        _friends = list;
        _lastFriendRefresh = _time.GetTimestamp();
        FriendsChanged?.Invoke();
    }
}
