using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;
using Horyzont.Net.Tests.Transport;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;

using Microsoft.Extensions.Logging;

namespace Horyzont.Net.Tests.Session;

/// <summary>Regression tests of the GPT-6.1 Sol review of K1-07.</summary>
public sealed class SessionHardeningTests
{
    private static readonly TimeSpan _fiveSeconds = TimeSpan.FromSeconds(5);

    [Fact]
    public void Receive_MalformedFrameAndThrowingLeftHook_KeepsServingOthers()
    {
        using var h = new SessionHarness();
        var bad = h.JoinInGame();
        var good = h.JoinInGame();
        var badPlayer = bad.Player(h);
        h.ServerHooks.ThrowOnLeft = true;
        var truncated = SessionHarness.Encode(new IntentSubmit(1, SessionHarness.SampleIntent())).AsSpan(0, 3);

        bad.Session.SendRaw(TransportChannel.ReliableOrdered, truncated);
        Assert.True(h.RunUntil(() => bad.Session.State == ClientState.Disconnected, _fiveSeconds));
        var seq = good.Session.SubmitIntent(SessionHarness.SampleIntent());

        Assert.True(h.RunUntil(() => good.Received<IntentReply>().Any(r => r.ClientSeq == seq), _fiveSeconds));
        Assert.Equal((badPlayer, DisconnectCode.ProtocolViolation), Assert.Single(h.ServerHooks.Left));
        Assert.Equal(ClientState.InGame, good.Session.State);
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains($"player {badPlayer.Value}", StringComparison.Ordinal));
    }

    [Fact]
    public void Receive_FloodAndThrowingLeftHook_KeepsServingOthers()
    {
        using var h = new SessionHarness();
        var flooder = h.JoinInGame();
        var other = h.JoinInGame();
        var flooderPlayer = flooder.Player(h);
        h.ServerHooks.ThrowOnLeft = true;
        var frame = SessionHarness.Encode(new TerrainChecksums(new PlanetId(1), []));

        uint lastSeq = 0;
        for (var ms = 0; ms < 1000; ms += 10)
        {
            for (var i = 0; i < 10 && flooder.Session.State == ClientState.InGame; i++)
            {
                flooder.Session.SendRaw(TransportChannel.ReliableOrdered, frame);
            }

            if (ms % 100 == 0)
            {
                lastSeq = other.Session.SubmitIntent(SessionHarness.SampleIntent());
            }

            h.Step();
        }

        Assert.True(h.RunUntil(() => flooder.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(2)));
        Assert.True(h.RunUntil(() => other.Received<IntentReply>().Any(r => r.ClientSeq == lastSeq), TimeSpan.FromSeconds(2)));
        Assert.Equal((flooderPlayer, DisconnectCode.Kicked), Assert.Single(h.ServerHooks.Left));
        Assert.Equal(10, other.Received<IntentReply>().Count());
        Assert.Equal(ClientState.InGame, other.Session.State);
    }

    [Fact]
    public void Shutdown_ThrowingLeftHook_ClosesEveryPlayer()
    {
        using var h = new SessionHarness();
        var a = h.JoinInGame();
        var b = h.JoinInGame();
        h.ServerHooks.ThrowOnLeft = true;

        h.Server.Shutdown(DisconnectCode.HostLeft);

        Assert.True(h.RunUntil(() => h.Server.IsShutdownComplete, _fiveSeconds));
        Assert.Equal(2, h.ServerHooks.Left.Count);
        Assert.True(h.RunUntil(() => a.Session.State == ClientState.Disconnected && b.Session.State == ClientState.Disconnected, _fiveSeconds));
    }

    [Fact]
    public void Connect_StormOfUnauthenticatedConnections_BoundedAndEstablishedClientServed()
    {
        using var h = new SessionHarness(new ServerOptions { BuildHash = SessionHarness.Build, MaxPlayers = 8 });
        var good = h.JoinInGame();
        using var attacker = h.Network.CreateTransport("attacker", h.TransportOptions);
        var attackerEvents = new RecordingEvents();
        var bound = h.Options.MaxPlayers + h.Options.MaxPendingConnections;

        for (var i = 0; i < 1000; i++)
        {
            attacker.Connect(new InMemoryEndpoint("server"));
        }

        var largest = 0;
        uint seq = 0;
        for (var step = 0; step < 100; step++)
        {
            attacker.Poll(attackerEvents);
            if (step % 10 == 0)
            {
                seq = good.Session.SubmitIntent(SessionHarness.SampleIntent());
            }

            h.Step();
            largest = Math.Max(largest, h.Server.ConnectionCount);
        }

        Assert.InRange(largest, 1, bound);
        Assert.True(h.RunUntil(() => good.Received<IntentReply>().Any(r => r.ClientSeq == seq), _fiveSeconds));
        Assert.Equal(ClientState.InGame, good.Session.State);
        Assert.Empty(h.ServerHooks.Left);
        Assert.True(attackerEvents.Disconnected.Count >= 1000 - bound, $"only {attackerEvents.Disconnected.Count} refused connections were closed");

        var late = h.AddClient();
        Assert.True(h.RunUntil(() => late.Session.State == ClientState.Disconnected, _fiveSeconds));
        Assert.Equal(HandshakeRejectCode.ServerFull, Assert.Single(late.Hooks.Rejections).Code);
    }

    [Fact]
    public void Receive_WallClockJumpsBack_RateLimitKeepsRefilling()
    {
        using var h = new SessionHarness();
        var client = h.JoinInGame();
        h.Clock.UtcOffset = -TimeSpan.FromHours(1);

        uint last = 0;
        for (var i = 0; i < 1000; i++)
        {
            last = client.Session.SubmitIntent(SessionHarness.SampleIntent());
            h.Step();
        }

        Assert.True(h.RunUntil(() => client.Received<IntentReply>().Any(r => r.ClientSeq == last), _fiveSeconds));
        Assert.Equal(ClientState.InGame, client.Session.State);
        Assert.Empty(h.ServerHooks.Left);
        Assert.Equal(0, h.Server.SilentlyDropped);
    }

    [Fact]
    public void Poll_WallClockJumpsForward_SessionsAndHandshakeSurvive()
    {
        using var h = new SessionHarness();
        var playing = h.JoinInGame();
        var joining = h.AddClient();
        h.Clock.UtcOffset = TimeSpan.FromHours(1);

        Assert.True(h.RunUntil(() => joining.Session.State == ClientState.Loading, _fiveSeconds));
        h.Run(TimeSpan.FromSeconds(1));

        Assert.Equal(ClientState.InGame, playing.Session.State);
        Assert.Empty(playing.Hooks.Disconnections);
        Assert.Empty(h.ServerHooks.Left);
    }

    [Fact]
    public void ClientReceive_UnsolicitedPong_NotAnRttSample()
    {
        using var h = new SessionHarness(conditions: Latency50Ms());
        var client = h.JoinInGame();
        Assert.True(h.RunUntil(() => client.Session.Rtt > TimeSpan.Zero, _fiveSeconds));

        InjectPong(h, client, new Pong(0, new Tick(1)));
        InjectPong(h, client, new Pong(1, new Tick(1)));
        h.Run(TimeSpan.FromMilliseconds(200));

        Assert.InRange(client.Session.Rtt, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(300));
        Assert.Equal(ClientState.InGame, client.Session.State);
    }

    [Fact]
    public void ClientReceive_DuplicatePong_CountsOnce()
    {
        using var h = new SessionHarness(conditions: Latency50Ms());
        var client = h.JoinInGame();
        Assert.True(h.RunUntil(() => client.Received<Pong>().Any(), _fiveSeconds));
        var first = client.Received<Pong>().First();
        h.Run(TimeSpan.FromSeconds(3));

        InjectPong(h, client, first);
        h.Run(TimeSpan.FromMilliseconds(200));

        Assert.InRange(client.Session.Rtt, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void ClientConnect_AfterReconnect_RttStartsEmpty()
    {
        using var h = new SessionHarness(conditions: Latency50Ms());
        var client = h.JoinInGame();
        Assert.True(h.RunUntil(() => client.Session.Rtt > TimeSpan.Zero, _fiveSeconds));
        client.Session.Disconnect();
        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, _fiveSeconds));

        client.Connect();

        Assert.Equal(TimeSpan.Zero, client.Session.Rtt);
    }

    private static LinkConditions Latency50Ms() => new(TimeSpan.FromMilliseconds(50), TimeSpan.Zero, 0, 0, 1);

    private static void InjectPong(SessionHarness h, TestClient client, Pong pong) =>
        h.ServerTransport.Send(h.Server.ConnectionOf(client.Player(h)), TransportChannel.Unreliable, SessionHarness.Encode(pong));
}
