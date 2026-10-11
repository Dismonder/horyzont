using Horyzont.Client.Session;
using Horyzont.Client.Steam;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Steam;

public sealed class SteamSessionBinderTests
{
    private static readonly Guid _world = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void ConfigureServerTransports_Host_AddsSteamTransportAndListenerAfterExistingOnes()
    {
        using var s = new SessionFixture();
        var local = new RecordingTransport();
        var transports = new List<ITransport> { local };
        var listen = new List<TransportEndpoint> { new InMemoryEndpoint("server") };

        s.Binder.ConfigureServerTransports(transports, listen);

        Assert.Equal(2, transports.Count);
        Assert.Same(local, transports[0]);
        Assert.Same(s.SteamTransports.Single(), transports[1]);
        Assert.Equal(new SteamEndpoint(s.Api.LocalSteamId), listen[^1]);
        Assert.Equal(2, listen.Count);
    }

    [Fact]
    public void ConfigureServerTransports_SteamUnavailable_LeavesLanTransportsAlone()
    {
        using var s = new SessionFixture(initialize: false);
        var transports = new List<ITransport> { new RecordingTransport() };
        var listen = new List<TransportEndpoint> { new InMemoryEndpoint("server") };

        s.Binder.ConfigureServerTransports(transports, listen);
        s.Binder.OnWorldStarted(new WorldId(_world), 8, false);

        Assert.Single(transports);
        Assert.Single(listen);
        Assert.Empty(s.SteamTransports);
        Assert.Empty(s.Api.CreateRequests);
    }

    [Fact]
    public void Host_WorldStartFails_DoesNotCreateLobby()
    {
        using var s = new SessionFixture();
        s.Binder.ConfigureServerTransports([new RecordingTransport()], [new InMemoryEndpoint("server")]);

        // A failed start never reaches OnWorldStarted; the composition goes straight to shutdown and release.
        s.Binder.OnShutdownRequested();
        s.Binder.OnTransportsReleased();

        Assert.Empty(s.Api.CreateRequests);
        Assert.Empty(s.Api.Left);
        Assert.DoesNotContain(s.Api.Calls, c => c.StartsWith("set:", StringComparison.Ordinal));
    }

    [Fact]
    public void Host_WorldStarted_CreatesFriendsOnlyLobbyWithPlanCapacityAndWorld()
    {
        using var s = new SessionFixture();
        s.Binder.ConfigureServerTransports([new RecordingTransport()], [new InMemoryEndpoint("server")]);

        s.Binder.OnWorldStarted(new WorldId(_world), 6, true);
        var lobby = s.Api.CompleteCreate();

        Assert.Equal((LobbyVisibility.FriendsOnly, 6), (s.Api.CreateRequests[0].Visibility, s.Api.CreateRequests[0].Capacity));
        Assert.Equal(_world.ToString("D"), s.Api.GetData(lobby, LobbyMetadata.World));
        Assert.Equal("1", s.Api.GetData(lobby, LobbyMetadata.Password));
        Assert.Equal("1", s.Api.GetData(lobby, LobbyMetadata.Ready));
        Assert.Equal(LobbyState.Hosting, s.Services.Lobby.State);
    }

    [Fact]
    public void Host_LobbyCreateFails_KeepsLanWorldRunning()
    {
        using var s = new SessionFixture();
        var transports = new List<ITransport> { new RecordingTransport() };
        s.Binder.ConfigureServerTransports(transports, [new InMemoryEndpoint("server")]);
        var ended = 0;
        s.Binder.Bind(() => ended++);

        s.Binder.OnWorldStarted(new WorldId(_world), 8, false);
        s.Api.RaiseCreated(new LobbyApiResult(s.Api.CreateRequests[0].RequestId, 0, LobbyError.PlatformFailure));

        Assert.Equal(LobbyState.Failed, s.Services.Lobby.State);
        Assert.Equal(0, ended);
        Assert.Equal(2, transports.Count);

        s.Services.Lobby.Host(LobbyVisibility.FriendsOnly, 8);
        Assert.Equal(2, s.Api.CreateRequests.Count);
    }

    [Fact]
    public void CreateClientTransport_LobbyJoin_UsesSteamTransportWithoutFallback()
    {
        using var s = new SessionFixture(new SteamSessionOptions { ConnectLobbyId = 5000 });
        var fallbackCalls = 0;

        var transport = s.Binder.CreateClientTransport(() => { fallbackCalls++; return new RecordingTransport(); });

        Assert.Same(s.SteamTransports.Single(), transport);
        Assert.Equal(0, fallbackCalls);
    }

    [Fact]
    public void CreateClientTransport_ConnectByAddress_UsesFallbackAndDoesNotTakeConnection()
    {
        using var s = new SessionFixture(new SteamSessionOptions { ConnectsByAddress = true });
        var fallback = new RecordingTransport();

        Assert.Same(fallback, s.Binder.CreateClientTransport(() => fallback));
        var client = s.NewClient(fallback);

        Assert.False(s.Binder.TryConnectClient(client));
        Assert.Empty(s.SteamTransports);
        Assert.Empty(s.Api.JoinRequests);
    }

    [Fact]
    public void Launch_ConnectLobbyArgument_UsesSameJoinFlow()
    {
        using var s = new SessionFixture(new SteamSessionOptions { ConnectLobbyId = 5000, Password = "pw" });
        var transport = (RecordingTransport)s.Binder.CreateClientTransport(() => new RecordingTransport());
        var client = s.NewClient(transport);
        s.Api.PublishLobby(5000, LobbyFixture.HostId, LobbyFixture.Build, LobbyFixture.Protocol, passwordRequired: true);

        Assert.True(s.Binder.TryConnectClient(client));
        Assert.Equal(ClientState.Disconnected, client.Session.State);
        Assert.Equal(5000UL, Assert.Single(s.Api.JoinRequests).LobbyId);

        s.Api.RaiseJoined(new LobbyApiResult(s.Api.JoinRequests[0].RequestId, 5000, LobbyError.None));

        Assert.Equal(ClientState.Connecting, client.Session.State);
        Assert.Equal(new SteamEndpoint(LobbyFixture.HostId), Assert.Single(transport.Connects));
        client.Poll();
        var hello = Assert.IsType<ClientHello>(MessageCodec.Read(Assert.Single(transport.Sent)));
        Assert.Equal(new PlatformIdentity(IdentityPlatform.Steam, s.Api.LocalSteamId), hello.Identity);
        Assert.Equal("pw", hello.Password);
        Assert.Equal(s.Api.DisplayName, hello.DisplayName);
    }

    [Fact]
    public void Launch_ConnectLobbyArgument_JoinFailureEndsTheSession()
    {
        using var s = new SessionFixture(new SteamSessionOptions { ConnectLobbyId = 5000 });
        var transport = (RecordingTransport)s.Binder.CreateClientTransport(() => new RecordingTransport());
        var client = s.NewClient(transport);
        var ended = 0;
        s.Binder.Bind(() => ended++);
        s.Binder.TryConnectClient(client);

        s.Api.RaiseJoined(new LobbyApiResult(s.Api.JoinRequests[0].RequestId, 0, LobbyError.Full));

        Assert.Equal(1, ended);
        Assert.Empty(transport.Connects);
    }

    [Fact]
    public void Client_OwnerChanged_EndsTheSession()
    {
        using var s = new SessionFixture(new SteamSessionOptions { ConnectLobbyId = 5000 });
        var transport = (RecordingTransport)s.Binder.CreateClientTransport(() => new RecordingTransport());
        var client = s.NewClient(transport);
        var ended = 0;
        s.Binder.Bind(() => ended++);
        s.Api.PublishLobby(5000, LobbyFixture.HostId, LobbyFixture.Build, LobbyFixture.Protocol);
        s.Binder.TryConnectClient(client);
        s.Api.RaiseJoined(new LobbyApiResult(s.Api.JoinRequests[0].RequestId, 5000, LobbyError.None));

        s.Api.SetOwner(5000, 1);
        s.Time.Advance(TimeSpan.FromSeconds(1));
        s.Services.Pump();

        Assert.Equal(1, ended);
    }

    [Fact]
    public void Shutdown_HostLeaves_DisconnectsPeersAndShutsSdkLast()
    {
        using var s = new SessionFixture();
        s.Binder.ConfigureServerTransports([new RecordingTransport()], [new InMemoryEndpoint("server")]);
        s.Binder.OnWorldStarted(new WorldId(_world), 8, false);
        var lobby = s.Api.CompleteCreate();
        var mark = s.Api.Calls.Count;

        s.Binder.OnShutdownRequested();
        s.Binder.OnShutdownRequested();
        Assert.Equal([$"set:{lobby}:horyzont_ready=0"], s.Api.Calls.Skip(mark).ToArray());
        Assert.Empty(s.Api.Left);

        s.Binder.OnTransportsReleased();
        s.Binder.OnTransportsReleased();
        Assert.Equal([$"set:{lobby}:horyzont_ready=0", $"leave:{lobby}"], s.Api.Calls.Skip(mark).ToArray());
        Assert.Equal(0, s.Api.ShutdownCount);

        Assert.True(s.Services.TryShutdown());
        Assert.True(s.Services.TryShutdown());
        Assert.Equal("shutdown", s.Api.Calls[^1]);
        Assert.Equal(1, s.Api.ShutdownCount);
        Assert.Equal(1, s.Api.Calls.Count(c => c == $"leave:{lobby}"));
    }

    [Fact]
    public void Shutdown_TransportDisposeTimesOut_DoesNotLeaveOrShutDownSdk()
    {
        using var s = new SessionFixture();
        s.Binder.ConfigureServerTransports([new RecordingTransport()], [new InMemoryEndpoint("server")]);
        s.Binder.OnWorldStarted(new WorldId(_world), 8, false);
        var lobby = s.Api.CompleteCreate();

        s.Binder.OnShutdownRequested();

        // OnTransportsReleased is never called: the dispose failed or timed out.
        Assert.Empty(s.Api.Left);
        Assert.Equal(0, s.Api.ShutdownCount);
        Assert.Equal(LobbyState.Hosting, s.Services.Lobby.State);
        Assert.Equal("1", s.Api.GetData(lobby, LobbyMetadata.Schema));
    }

    [Fact]
    public void Shutdown_PendingJoin_IsCancelledAndLateSuccessLeaves()
    {
        using var s = new SessionFixture(new SteamSessionOptions { ConnectLobbyId = 5000 });
        var transport = (RecordingTransport)s.Binder.CreateClientTransport(() => new RecordingTransport());
        s.Binder.TryConnectClient(s.NewClient(transport));
        var request = s.Api.JoinRequests[0].RequestId;

        s.Binder.OnShutdownRequested();
        s.Api.PublishLobby(5000, LobbyFixture.HostId, LobbyFixture.Build, LobbyFixture.Protocol);
        s.Api.RaiseJoined(new LobbyApiResult(request, 5000, LobbyError.None));

        Assert.Empty(transport.Connects);
        Assert.Equal([5000UL], s.Api.Left);
    }

    [Fact]
    public void Shutdown_ReturnToMenu_KeepsSdkAlive()
    {
        using var s = new SessionFixture();
        for (var session = 0; session < 2; session++)
        {
            var binder = s.NewBinder(new SteamSessionOptions());
            binder.ConfigureServerTransports([new RecordingTransport()], [new InMemoryEndpoint("server")]);
            binder.OnWorldStarted(new WorldId(_world), 8, false);
            s.Api.CompleteCreate();
            binder.OnShutdownRequested();
            binder.OnTransportsReleased();
        }

        Assert.Equal(0, s.Api.ShutdownCount);
        Assert.Equal(1, s.Api.InitCount);
        Assert.Equal(2, s.Api.CreateRequests.Count);
        Assert.False(s.Services.SessionActive);
    }

    [Fact]
    public void SteamLifetime_MultipleSessions_InitializesAndPumpsOnce()
    {
        using var s = new SessionFixture();
        var before = s.Api.PumpCount;

        s.Services.Pump();
        s.Services.Pump();

        Assert.Equal(1, s.Api.InitCount);
        Assert.Equal(before + 2, s.Api.PumpCount);
    }

    [Fact]
    public void Create_InitializeFails_ServicesAreUnavailable()
    {
        using var s = new SessionFixture(initialize: false);

        Assert.False(s.Services.Available);
        Assert.Equal(LobbyState.Unavailable, s.Services.Lobby.State);
        Assert.True(s.Services.TryShutdown());
        Assert.Equal(0, s.Api.ShutdownCount);
    }

    private sealed class SessionFixture : IDisposable
    {
        public SessionFixture(SteamSessionOptions? options = null, bool initialize = true)
        {
            Api.InitializeResult = initialize;
            Services = SteamPlatformServices.Create(
                Api, 480, LobbyFixture.Build, LobbyFixture.Protocol, Time, NullLoggerFactory.Instance,
                () =>
                {
                    var transport = new RecordingTransport();
                    SteamTransports.Add(transport);
                    return transport;
                });
            Binder = NewBinder(options ?? new SteamSessionOptions());
        }

        public FakeSteamLobbyApi Api { get; } = new();

        public FakeTimeProvider Time { get; } = new();

        public SteamPlatformServices Services { get; }

        public SteamSessionBinder Binder { get; }

        public List<RecordingTransport> SteamTransports { get; } = [];

        public SteamSessionBinder NewBinder(SteamSessionOptions options)
        {
            return new SteamSessionBinder(Services, options, NullLogger<SteamSessionBinder>.Instance);
        }

        public ClientRuntime NewClient(ITransport transport) =>
            new(transport, new ClientOptions { BuildHash = 424242 }, false, Time, NullLoggerFactory.Instance);

        public void Dispose()
        {
            foreach (var transport in SteamTransports)
            {
                transport.Dispose();
            }
        }
    }
}
