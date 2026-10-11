using System.Globalization;

using Horyzont.Client.Steam;
using Horyzont.Net.Transport;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Steam;

/// <summary>A controller over a fake Steam API with a controllable clock and session flag.</summary>
internal sealed class LobbyFixture : IDisposable
{
    public const string Build = "424242";
    public const ushort Protocol = 3;
    public const ulong HostId = 76561198000000777UL;

    public LobbyFixture()
    {
        Controller = new SteamLobbyController(Api, Time, () => SessionActive, NullLogger<SteamLobbyController>.Instance);
        Controller.Configure(Build, Protocol);
        Controller.ConnectRequested += (endpoint, password) => Connects.Add((endpoint, password));
        Controller.Failed += error => Errors.Add(error);
        Controller.StateChanged += state => States.Add(state);
        Controller.JoinConfirmationRequested += id => Confirmations.Add(id);
    }

    public FakeSteamLobbyApi Api { get; } = new();

    public FakeTimeProvider Time { get; } = new();

    public SteamLobbyController Controller { get; }

    public bool SessionActive { get; set; }

    public List<(SteamEndpoint Endpoint, bool Password)> Connects { get; } = [];

    public List<LobbyError> Errors { get; } = [];

    public List<LobbyState> States { get; } = [];

    public List<ulong> Confirmations { get; } = [];

    public static string Dec(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Starts a join of a valid foreign lobby and completes the call successfully.</summary>
    public ulong JoinValid(ulong lobbyId = 5000, bool password = false)
    {
        Api.PublishLobby(lobbyId, HostId, Build, Protocol, password);
        Controller.Join(lobbyId);
        Api.RaiseJoined(new LobbyApiResult(Api.JoinRequests[^1].RequestId, lobbyId, LobbyError.None));
        return lobbyId;
    }

    public void Dispose() => Controller.Dispose();
}
