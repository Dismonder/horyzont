using System.Globalization;

using Horyzont.Client.Steam;

namespace Horyzont.Testing;

/// <summary>Deterministic stand-in for the Steam lobby SDK: records calls, completes asynchronous calls only when the test says so.</summary>
public sealed class FakeSteamLobbyApi : ISteamLobbyApi
{
    private readonly Dictionary<ulong, Dictionary<string, string>> _data = [];
    private readonly Dictionary<ulong, ulong> _owners = [];
    private readonly Dictionary<ulong, int> _members = [];
    private ulong _nextLobby = 1000;

    /// <summary>Gets the ordered log of calls, for example <c>create:Public:8:1</c>, <c>set:1000:horyzont_ready=1</c>, <c>leave:1000</c>.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Gets or sets the result of <see cref="TryInitialize"/>.</summary>
    public bool InitializeResult { get; set; } = true;

    /// <summary>Gets the number of <see cref="TryInitialize"/> calls.</summary>
    public int InitCount { get; private set; }

    /// <summary>Gets the number of <see cref="Pump"/> calls.</summary>
    public int PumpCount { get; private set; }

    /// <summary>Gets the number of <see cref="Shutdown"/> calls.</summary>
    public int ShutdownCount { get; private set; }

    /// <summary>Gets or sets the result of every <see cref="SetData"/>.</summary>
    public bool SetDataResult { get; set; } = true;

    /// <summary>Gets the pending create calls (visibility, capacity, request id).</summary>
    public List<(LobbyVisibility Visibility, int Capacity, uint RequestId)> CreateRequests { get; } = [];

    /// <summary>Gets the pending join calls.</summary>
    public List<(ulong LobbyId, uint RequestId)> JoinRequests { get; } = [];

    /// <summary>Gets the lobbies left, in order.</summary>
    public List<ulong> Left { get; } = [];

    /// <summary>Gets the lobbies whose invite overlay was opened.</summary>
    public List<ulong> Overlays { get; } = [];

    /// <summary>Gets or sets the application id given to <see cref="TryInitialize"/>.</summary>
    public uint AppId { get; set; } = 480;

    /// <summary>Gets the friends known to Steam, including those of other applications.</summary>
    public List<(ulong SteamId, string Name, uint AppId, ulong LobbyId)> FriendsInGames { get; } = [];

    /// <inheritdoc />
    public event Action<LobbyApiResult>? LobbyCreated;

    /// <inheritdoc />
    public event Action<LobbyApiResult>? LobbyJoined;

    /// <inheritdoc />
    public event Action<ulong>? JoinRequested;

    /// <inheritdoc />
    public ulong LocalSteamId { get; set; } = 76561198000000001UL;

    /// <inheritdoc />
    public string DisplayName { get; set; } = "Tester";

    /// <summary>Gets the number of subscribers of every event (leak checks).</summary>
    public int SubscriberCount =>
        (LobbyCreated?.GetInvocationList().Length ?? 0) + (LobbyJoined?.GetInvocationList().Length ?? 0) + (JoinRequested?.GetInvocationList().Length ?? 0);

    /// <inheritdoc />
    public bool TryInitialize(uint appId)
    {
        InitCount++;
        AppId = appId;
        Calls.Add("init:" + appId.ToString(CultureInfo.InvariantCulture));
        return InitializeResult;
    }

    /// <inheritdoc />
    public void Pump() => PumpCount++;

    /// <inheritdoc />
    public void Shutdown()
    {
        ShutdownCount++;
        Calls.Add("shutdown");
    }

    /// <inheritdoc />
    public void CreateLobby(LobbyVisibility visibility, int capacity, uint requestId)
    {
        CreateRequests.Add((visibility, capacity, requestId));
        Calls.Add(string.Create(CultureInfo.InvariantCulture, $"create:{visibility}:{capacity}:{requestId}"));
    }

    /// <inheritdoc />
    public void JoinLobby(ulong lobbyId, uint requestId)
    {
        JoinRequests.Add((lobbyId, requestId));
        Calls.Add(string.Create(CultureInfo.InvariantCulture, $"join:{lobbyId}:{requestId}"));
    }

    /// <inheritdoc />
    public void LeaveLobby(ulong lobbyId)
    {
        Left.Add(lobbyId);
        Calls.Add(string.Create(CultureInfo.InvariantCulture, $"leave:{lobbyId}"));
    }

    /// <inheritdoc />
    public bool SetData(ulong lobbyId, string key, string value)
    {
        Calls.Add(string.Create(CultureInfo.InvariantCulture, $"set:{lobbyId}:{key}={value}"));
        if (!SetDataResult)
        {
            return false;
        }

        DataOf(lobbyId)[key] = value;
        return true;
    }

    /// <inheritdoc />
    public string GetData(ulong lobbyId, string key) =>
        _data.TryGetValue(lobbyId, out var map) && map.TryGetValue(key, out var value) ? value : "";

    /// <inheritdoc />
    public ulong GetOwner(ulong lobbyId) => _owners.TryGetValue(lobbyId, out var owner) ? owner : 0;

    /// <inheritdoc />
    public int GetMemberCount(ulong lobbyId) => _members.TryGetValue(lobbyId, out var count) ? count : 0;

    /// <inheritdoc />
    public bool ShowInviteOverlay(ulong lobbyId)
    {
        Overlays.Add(lobbyId);
        Calls.Add(string.Create(CultureInfo.InvariantCulture, $"overlay:{lobbyId}"));
        return true;
    }

    /// <inheritdoc />
    public int CopyJoinableFriends(Span<SteamFriend> destination)
    {
        var count = 0;
        foreach (var friend in FriendsInGames)
        {
            if (friend.AppId != AppId || friend.LobbyId == 0 || count == destination.Length)
            {
                continue;
            }

            destination[count++] = new SteamFriend(friend.SteamId, friend.Name, friend.LobbyId);
        }

        return count;
    }

    /// <summary>Completes the create call at the given index successfully with a fresh lobby id that the local user owns.</summary>
    /// <param name="index">Index in <see cref="CreateRequests"/>.</param>
    /// <returns>The new lobby id.</returns>
    public ulong CompleteCreate(int index = -1)
    {
        var request = CreateRequests[index < 0 ? CreateRequests.Count - 1 : index];
        var lobby = _nextLobby++;
        _owners[lobby] = LocalSteamId;
        _members[lobby] = 1;
        LobbyCreated?.Invoke(new LobbyApiResult(request.RequestId, lobby, LobbyError.None));
        return lobby;
    }

    /// <summary>Raises a create result.</summary>
    /// <param name="result">Result.</param>
    public void RaiseCreated(LobbyApiResult result) => LobbyCreated?.Invoke(result);

    /// <summary>Raises a join result.</summary>
    /// <param name="result">Result.</param>
    public void RaiseJoined(LobbyApiResult result) => LobbyJoined?.Invoke(result);

    /// <summary>Raises an invite or friend-join request.</summary>
    /// <param name="lobbyId">Lobby id.</param>
    public void RaiseJoinRequested(ulong lobbyId) => JoinRequested?.Invoke(lobbyId);

    /// <summary>Publishes a lobby as another host would, with valid metadata.</summary>
    /// <param name="lobbyId">Lobby id.</param>
    /// <param name="host">Host SteamID, also the owner.</param>
    /// <param name="build">Build hash (decimal text).</param>
    /// <param name="protocol">Protocol version.</param>
    /// <param name="passwordRequired">Whether the host requires a password.</param>
    /// <param name="members">Members.</param>
    public void PublishLobby(ulong lobbyId, ulong host, string build, ushort protocol, bool passwordRequired = false, int members = 2)
    {
        _owners[lobbyId] = host;
        _members[lobbyId] = members;
        var map = DataOf(lobbyId);
        map[LobbyMetadata.Schema] = LobbyMetadata.SchemaValue;
        map[LobbyMetadata.Host] = host.ToString(CultureInfo.InvariantCulture);
        map[LobbyMetadata.Build] = build;
        map[LobbyMetadata.Protocol] = protocol.ToString(CultureInfo.InvariantCulture);
        map[LobbyMetadata.World] = Guid.Parse("11111111-2222-3333-4444-555555555555").ToString("D");
        map[LobbyMetadata.Capacity] = "8";
        map[LobbyMetadata.Password] = passwordRequired ? "1" : "0";
        map[LobbyMetadata.Ready] = "1";
    }

    /// <summary>Overwrites or removes (null) one metadata value of a lobby.</summary>
    /// <param name="lobbyId">Lobby id.</param>
    /// <param name="key">Key.</param>
    /// <param name="value">New value, or null to remove.</param>
    public void Corrupt(ulong lobbyId, string key, string? value)
    {
        if (value is null)
        {
            DataOf(lobbyId).Remove(key);
        }
        else
        {
            DataOf(lobbyId)[key] = value;
        }
    }

    /// <summary>Changes the owner of a lobby.</summary>
    /// <param name="lobbyId">Lobby id.</param>
    /// <param name="owner">New owner.</param>
    public void SetOwner(ulong lobbyId, ulong owner) => _owners[lobbyId] = owner;

    private Dictionary<string, string> DataOf(ulong lobbyId)
    {
        if (!_data.TryGetValue(lobbyId, out var map))
        {
            map = [];
            _data[lobbyId] = map;
        }

        return map;
    }
}
