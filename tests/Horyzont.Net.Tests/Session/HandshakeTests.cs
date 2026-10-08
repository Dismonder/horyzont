using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;

namespace Horyzont.Net.Tests.Session;

public sealed class HandshakeTests
{
    public enum RejectCase
    {
        WrongBuild,
        BadPassword,
        IdentityMismatchSteamId,
        IdentityMismatchDevOverSteam,
        Banned,
        AlreadyConnected,
        ServerFull,
    }

    [Fact]
    public void Handshake_ValidHello_ReachesInGame()
    {
        using var h = new SessionHarness();

        var client = h.JoinInGame();

        Assert.Equal(ClientState.InGame, client.Session.State);
        Assert.Equal(["OnPlayerJoining", "CreateHello", "OnPlayerReady"], h.ServerHooks.Calls);
        Assert.Equal(client.Player(h), client.Hooks.Hello!.YourPlayer);
        Assert.Equal([client.Player(h)], h.Server.PlayersInGame);
    }

    [Theory]
    [InlineData(RejectCase.WrongBuild, HandshakeRejectCode.WrongBuild)]
    [InlineData(RejectCase.BadPassword, HandshakeRejectCode.BadPassword)]
    [InlineData(RejectCase.IdentityMismatchSteamId, HandshakeRejectCode.IdentityMismatch)]
    [InlineData(RejectCase.IdentityMismatchDevOverSteam, HandshakeRejectCode.IdentityMismatch)]
    [InlineData(RejectCase.Banned, HandshakeRejectCode.Banned)]
    [InlineData(RejectCase.AlreadyConnected, HandshakeRejectCode.AlreadyConnected)]
    [InlineData(RejectCase.ServerFull, HandshakeRejectCode.ServerFull)]
    public void Handshake_InvalidHello_RejectsWithCode(RejectCase scenario, HandshakeRejectCode expected)
    {
        var banned = SessionHarness.DevIdentity(666);
        var options = new ServerOptions
        {
            BuildHash = SessionHarness.Build,
            Password = scenario == RejectCase.BadPassword ? "secret" : "",
            BannedIdentities = new HashSet<PlatformIdentity> { banned },
            MaxPlayers = scenario == RejectCase.ServerFull ? 1 : 8,
        };
        using var h = new SessionHarness(options);
        var existing = SessionHarness.DevIdentity(1);
        if (scenario is RejectCase.AlreadyConnected or RejectCase.ServerFull)
        {
            h.JoinInGame(existing);
        }

        var joinsBefore = h.ServerHooks.Joined.Count;
        var client = scenario switch
        {
            RejectCase.WrongBuild => h.AddClient(buildHash: SessionHarness.Build + 1),
            RejectCase.BadPassword => h.AddClient(password: "wrong"),
            RejectCase.IdentityMismatchSteamId => h.AddClient(new PlatformIdentity(IdentityPlatform.Steam, 222), fakeSteamId: 111),
            RejectCase.IdentityMismatchDevOverSteam => h.AddClient(new PlatformIdentity(IdentityPlatform.Dev, 111), fakeSteamId: 111),
            RejectCase.Banned => h.AddClient(banned),
            RejectCase.AlreadyConnected => h.AddClient(existing),
            RejectCase.ServerFull => h.AddClient(SessionHarness.DevIdentity(2)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(5)));

        Assert.Equal(expected, Assert.Single(client.Hooks.Rejections).Code);
        Assert.Single(client.Hooks.Disconnections);
        Assert.Equal(joinsBefore, h.ServerHooks.Joined.Count);
        Assert.Null(client.Hooks.Hello);
    }

    [Fact]
    public void Handshake_MatchingSteamIdentity_IsAccepted()
    {
        using var h = new SessionHarness();
        var client = h.AddClient(new PlatformIdentity(IdentityPlatform.Steam, 111), fakeSteamId: 111);

        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Loading, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Handshake_CorrectPassword_IsAccepted()
    {
        using var h = new SessionHarness(new ServerOptions { BuildHash = SessionHarness.Build, Password = "secret" });
        var client = h.AddClient(password: "secret");

        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Loading, TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData((ushort)2, (byte)9)]
    [InlineData((ushort)2, (byte)1)]
    [InlineData((ushort)ProtocolConstants.ProtocolVersion, (byte)9)]
    public void Handshake_NewerProtocolHello_RejectsWrongProtocol(ushort protocolVersion, byte formatVersion)
    {
        using var h = new SessionHarness();
        var raw = h.AddRaw();
        Assert.True(h.RunUntil(() => raw.IsConnected, TimeSpan.FromSeconds(1)));

        // Frozen prefix (type, format version, protocol version) followed by a body the server cannot know.
        byte[] frame = [(byte)MessageType.ClientHello, formatVersion, (byte)protocolVersion, (byte)(protocolVersion >> 8), 0xFF, 0xEE, 0xDD];
        raw.Send(TransportChannel.ReliableOrdered, frame);

        Assert.True(h.RunUntil(() => raw.IsClosed, TimeSpan.FromSeconds(5)));
        var reject = Assert.IsType<HandshakeReject>(Assert.Single(raw.Messages()));
        Assert.Equal(HandshakeRejectCode.WrongProtocol, reject.Code);
        Assert.Empty(h.ServerHooks.Joined);
    }

    [Fact]
    public void Handshake_CurrentVersionUndecodableHello_DisconnectsProtocolViolation()
    {
        using var h = new SessionHarness();
        var raw = h.AddRaw();
        Assert.True(h.RunUntil(() => raw.IsConnected, TimeSpan.FromSeconds(1)));
        var valid = SessionHarness.Encode(new ClientHello(
            ProtocolConstants.ProtocolVersion, SessionHarness.Build, SessionHarness.DevIdentity(5), "x", "", []));

        raw.Send(TransportChannel.ReliableOrdered, valid[..^1]);

        Assert.True(h.RunUntil(() => raw.IsClosed, TimeSpan.FromSeconds(5)));
        Assert.Equal(DisconnectCode.ProtocolViolation, Assert.IsType<Disconnect>(Assert.Single(raw.Messages())).Code);
    }

    [Fact]
    public void Poll_NoHelloFor15s_DisconnectsHandshakeTimeout()
    {
        using var h = new SessionHarness();
        var raw = h.AddRaw();
        Assert.True(h.RunUntil(() => raw.IsConnected, TimeSpan.FromSeconds(1)));

        h.Run(TimeSpan.FromSeconds(14.9));
        Assert.Empty(raw.Events.Received);

        Assert.True(h.RunUntil(() => raw.IsClosed, TimeSpan.FromSeconds(2)));
        Assert.Equal(DisconnectCode.HandshakeTimeout, Assert.IsType<Disconnect>(Assert.Single(raw.Messages())).Code);
    }

    [Fact]
    public void Handshake_NinthClient_RejectsServerFull()
    {
        using var h = new SessionHarness();
        var players = Enumerable.Range(0, 8).Select(_ => h.JoinInGame()).ToList();

        var ninth = h.AddClient();
        Assert.True(h.RunUntil(() => ninth.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(5)));

        Assert.Equal(HandshakeRejectCode.ServerFull, Assert.Single(ninth.Hooks.Rejections).Code);
        Assert.All(players, p => Assert.Equal(ClientState.InGame, p.Session.State));
        Assert.Equal(8, h.Server.PlayersInGame.Count);
    }

    [Fact]
    public void Handshake_ClientWithoutServerHello_TimesOutLocally()
    {
        using var h = new SessionHarness(transportOptions: new TransportOptions(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(1))) { PollServer = false };
        var client = h.AddClient();

        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(16)));

        Assert.Equal(DisconnectCode.HandshakeTimeout, Assert.Single(client.Hooks.Disconnections).Code);
    }
}
