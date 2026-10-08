using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;

using Microsoft.Extensions.Logging;

namespace Horyzont.Net.Tests.Session;

public sealed class ProtocolRulesTests
{
    public enum WrongChannelCase
    {
        InputBatchReliable,
        IntentSubmitUnreliable,
    }

    [Fact]
    public void Receive_ReliableNotAllowedInState_DisconnectsProtocolViolation()
    {
        using var h = new SessionHarness();
        var client = h.AddClient();
        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Loading, TimeSpan.FromSeconds(5)));
        var player = client.Player(h);

        client.Session.SendRaw(TransportChannel.ReliableOrdered, SessionHarness.Encode(new IntentSubmit(1, SessionHarness.SampleIntent())));

        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(5)));
        Assert.Equal(DisconnectCode.ProtocolViolation, Assert.Single(client.Hooks.Disconnections).Code);
        Assert.Equal((player, DisconnectCode.ProtocolViolation), Assert.Single(h.ServerHooks.Left));
        Assert.Empty(h.ServerHooks.Intents);
    }

    [Fact]
    public void Receive_UnreliableBeforeReady_DroppedSilently()
    {
        var jitter = new LinkConditions(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(60), 0, 0, 7);
        using var h = new SessionHarness(conditions: jitter);
        var client = h.AddClient();
        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Loading, TimeSpan.FromSeconds(5)));

        client.Session.SendReady();
        for (uint i = 0; i < 20; i++)
        {
            client.Session.SendInputs(1 + i, [new PlayerInput(i, 0, 0, 0, 0, InputButtons.None)]);
        }

        var player = client.Player(h);
        Assert.True(h.RunUntil(() => h.ServerHooks.Ready.Contains(player), TimeSpan.FromSeconds(5)));
        h.Run(TimeSpan.FromSeconds(1));

        Assert.True(h.Server.SilentlyDropped > 0, "no InputBatch overtook ClientReady; pick another seed");
        Assert.Equal(ClientState.InGame, client.Session.State);
        Assert.Empty(client.Hooks.Disconnections);
        Assert.Empty(h.ServerHooks.Left);
    }

    [Fact]
    public void Receive_MalformedFrame_DisconnectsAndKeepsServingOthers()
    {
        using var h = new SessionHarness();
        var bad = h.JoinInGame();
        var good = h.JoinInGame();
        var badPlayer = bad.Player(h);
        var random = new Random(42);
        var junk = new byte[200];
        random.NextBytes(junk);
        junk[0] = 0xEE;

        bad.Session.SendRaw(TransportChannel.ReliableOrdered, junk);
        Assert.True(h.RunUntil(() => bad.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(5)));

        Assert.Equal(DisconnectCode.ProtocolViolation, Assert.Single(bad.Hooks.Disconnections).Code);
        Assert.Contains(h.Logger.Entries, e => e.Level >= LogLevel.Warning && e.Message.Contains($"player {badPlayer.Value}", StringComparison.Ordinal));

        var seq = good.Session.SubmitIntent(SessionHarness.SampleIntent());
        Assert.True(h.RunUntil(() => good.Received<IntentReply>().Any(), TimeSpan.FromSeconds(5)));
        Assert.Equal(seq, good.Received<IntentReply>().Single().ClientSeq);
        Assert.Equal(ClientState.InGame, good.Session.State);
    }

    [Fact]
    public void Receive_RandomFramesInEveryState_NeverThrows()
    {
        using var h = new SessionHarness(new ServerOptions { BuildHash = SessionHarness.Build, MaxPlayers = 64 });
        var random = new Random(1234);
        for (var i = 0; i < 40; i++)
        {
            var client = h.AddClient();
            var target = (ClientState)(2 + (i % 3));
            if (target != ClientState.AwaitingServerHello)
            {
                Assert.True(h.RunUntil(() => client.Session.State == ClientState.Loading, TimeSpan.FromSeconds(5)));
            }
            else
            {
                Assert.True(h.RunUntil(() => client.Session.State == ClientState.AwaitingServerHello, TimeSpan.FromSeconds(5)));
            }

            if (target == ClientState.InGame)
            {
                client.Session.SendReady();
            }

            var junk = new byte[random.Next(0, 300)];
            random.NextBytes(junk);
            if (junk.Length > 0 && i % 2 == 0)
            {
                junk[0] = (byte)random.Next(1, 21); // a known type with a random body
            }

            client.Session.SendRaw(i % 4 == 0 ? TransportChannel.Unreliable : TransportChannel.ReliableOrdered, junk.Length > 1150 ? junk[..1150] : junk);
            h.Run(TimeSpan.FromMilliseconds(50));
        }

        h.Run(TimeSpan.FromSeconds(2));
        var survivor = h.JoinInGame();
        Assert.Equal(ClientState.InGame, survivor.Session.State);
    }

    [Theory]
    [InlineData(WrongChannelCase.InputBatchReliable)]
    [InlineData(WrongChannelCase.IntentSubmitUnreliable)]
    public void Receive_WrongChannel_DisconnectsProtocolViolation(WrongChannelCase scenario)
    {
        using var h = new SessionHarness();
        var client = h.JoinInGame();
        var player = client.Player(h);

        if (scenario == WrongChannelCase.InputBatchReliable)
        {
            var batch = new InputBatch(1, [new PlayerInput(1, 0, 0, 0, 0, InputButtons.None)]);
            client.Session.SendRaw(TransportChannel.ReliableOrdered, SessionHarness.Encode(batch));
        }
        else
        {
            client.Session.SendRaw(TransportChannel.Unreliable, SessionHarness.Encode(new IntentSubmit(1, SessionHarness.SampleIntent())));
        }

        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(5)));
        Assert.Equal((player, DisconnectCode.ProtocolViolation), Assert.Single(h.ServerHooks.Left));
        Assert.Empty(h.ServerHooks.Intents);
    }

    [Theory]
    [InlineData(MessageType.ConstructState)]
    [InlineData(MessageType.ServerHello)]
    [InlineData(MessageType.TerrainBlockData)]
    public void Receive_ServerToClientTypeFromClient_DisconnectsWithoutDecoding(MessageType type)
    {
        using var h = new SessionHarness();
        var client = h.JoinInGame();
        var decodedBefore = h.Server.FramesDecoded;

        // A type byte the server never receives, with a body that would be expensive to decode.
        var frame = new byte[140];
        frame[0] = (byte)type;
        frame[1] = 1;
        client.Session.SendRaw(TransportChannel.ReliableOrdered, frame);

        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(5)));
        Assert.Equal(DisconnectCode.ProtocolViolation, Assert.Single(client.Hooks.Disconnections).Code);
        Assert.Equal(decodedBefore, h.Server.FramesDecoded);
    }

    [Fact]
    public void SubmitIntent_ServerReplies_ReplyCarriesClientSeq()
    {
        using var h = new SessionHarness();
        var client = h.JoinInGame();

        var first = client.Session.SubmitIntent(SessionHarness.SampleIntent());
        var second = client.Session.SubmitIntent(SessionHarness.SampleIntent());
        Assert.True(h.RunUntil(() => client.Received<IntentReply>().Count() == 2, TimeSpan.FromSeconds(5)));

        Assert.NotEqual(first, second);
        Assert.Equal([first, second], h.ServerHooks.Intents.Select(i => i.ClientSeq));
        Assert.Equal([first, second], client.Received<IntentReply>().Select(r => r.ClientSeq));
        Assert.Equal(new OpId(first + 1000), client.Received<IntentReply>().First().AppliedOpId);
    }

    [Fact]
    public void Receive_Flood_KicksOnlyFlooder()
    {
        using var h = new SessionHarness();
        var flooder = h.JoinInGame();
        var other = h.JoinInGame();
        var flooderPlayer = flooder.Player(h);
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

        Assert.Equal(DisconnectCode.Kicked, Assert.Single(flooder.Hooks.Disconnections).Code);
        Assert.Equal((flooderPlayer, DisconnectCode.Kicked), Assert.Single(h.ServerHooks.Left));
        Assert.Equal(10, other.Received<IntentReply>().Count());
        Assert.Equal(ClientState.InGame, other.Session.State);
        Assert.Empty(other.Hooks.Disconnections);
    }

    [Fact]
    public void Kick_Player_ClientReceivesCodeBeforeClose()
    {
        using var h = new SessionHarness();
        var client = h.JoinInGame();
        var player = client.Player(h);

        h.Server.Kick(player, DisconnectCode.Kicked);
        Assert.Equal((player, DisconnectCode.Kicked), Assert.Single(h.ServerHooks.Left));
        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(5)));

        var (code, stateAtCall) = Assert.Single(client.Hooks.Disconnections);
        Assert.Equal(DisconnectCode.Kicked, code);
        Assert.Equal(ClientState.Closing, stateAtCall);
        Assert.Empty(h.Server.PlayersInGame);
    }

    [Fact]
    public void Poll_SilenceFor10sInGame_ReportsPlayerLeftTimeout()
    {
        // The transport timeout is longer than the session idle timeout, so the session itself must notice.
        using var h = new SessionHarness(transportOptions: new TransportOptions(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(1)));
        var client = h.JoinInGame();
        var player = client.Player(h);
        client.Polling = false;
        client.PumpTransportOnly = true;

        h.Run(TimeSpan.FromSeconds(9.5));
        Assert.Empty(h.ServerHooks.Left);

        Assert.True(h.RunUntil(() => h.ServerHooks.Left.Count > 0, TimeSpan.FromSeconds(1)));
        Assert.Equal((player, DisconnectCode.Timeout), Assert.Single(h.ServerHooks.Left));
    }

    [Fact]
    public void Shutdown_HostLeft_NotifiesAllAndCompletes()
    {
        using var h = new SessionHarness();
        var clients = Enumerable.Range(0, 3).Select(_ => h.JoinInGame()).ToList();

        h.Server.Shutdown(DisconnectCode.HostLeft);
        Assert.False(h.Server.IsShutdownComplete);
        Assert.True(h.RunUntil(() => h.Server.IsShutdownComplete, TimeSpan.FromSeconds(5)));

        Assert.All(clients, c => Assert.Equal(DisconnectCode.HostLeft, Assert.Single(c.Hooks.Disconnections).Code));
        Assert.Equal(3, h.ServerHooks.Left.Count);
        Assert.All(h.ServerHooks.Left, l => Assert.Equal(DisconnectCode.HostLeft, l.Reason));
    }

    [Fact]
    public void Shutdown_DuringHandshake_ClientReportsHostLeft()
    {
        var slow = new LinkConditions(TimeSpan.FromMilliseconds(100), TimeSpan.Zero, 0, 0, 1);
        using var h = new SessionHarness(conditions: slow);
        var client = h.AddClient();
        Assert.True(h.RunUntil(() => client.Session.State == ClientState.AwaitingServerHello, TimeSpan.FromSeconds(5)));

        h.Server.Shutdown(DisconnectCode.HostLeft);
        Assert.True(h.RunUntil(() => h.Server.IsShutdownComplete && client.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(5)));

        Assert.Equal(DisconnectCode.HostLeft, Assert.Single(client.Hooks.Disconnections).Code);
        Assert.Empty(h.ServerHooks.Joined);
    }

    [Fact]
    public void ClientReceive_UnreliableBeforeServerHello_DroppedSilently()
    {
        var jitter = new LinkConditions(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(60), 0, 0, 3);
        using var h = new SessionHarness(conditions: jitter);
        var client = h.AddClient();
        Assert.True(h.RunUntil(() => h.ServerHooks.Joined.Count == 1, TimeSpan.FromSeconds(5)));
        Assert.Equal(ClientState.AwaitingServerHello, client.Session.State);

        var player = h.ServerHooks.Joined[0].Player;
        for (var i = 0; i < 20; i++)
        {
            h.Server.Send(player, new Pong((ulong)i, new Tick(5)));
        }

        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Loading, TimeSpan.FromSeconds(5)));
        client.Session.SendReady();
        h.Run(TimeSpan.FromSeconds(1));

        Assert.True(client.Session.SilentlyDropped > 0, "no Pong overtook ServerHello; pick another seed");
        Assert.Equal(ClientState.InGame, client.Session.State);
        Assert.Empty(client.Hooks.Disconnections);
    }

    [Fact]
    public void ClientReceive_ClientToServerTypeReliable_EndsSessionWithoutDecoding()
    {
        using var h = new SessionHarness();
        var client = h.JoinInGame();
        var player = client.Player(h);
        var decodedBefore = client.Session.FramesDecoded;

        // The server must never send ClientHello; the client rejects the type byte before decoding.
        h.ServerTransport.Send(FirstServerConnection(h), TransportChannel.ReliableOrdered, [(byte)MessageType.ClientHello, 1, 1, 0]);

        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(5)));
        Assert.Equal(DisconnectCode.ProtocolViolation, Assert.Single(client.Hooks.Disconnections).Code);
        Assert.Equal(decodedBefore, client.Session.FramesDecoded);
        Assert.Equal((player, DisconnectCode.ProtocolViolation), Assert.Single(h.ServerHooks.Left));
    }

    [Fact]
    public void ClientReceive_HelloRejectedByGame_EndsSessionWithProtocolViolation()
    {
        using var h = new SessionHarness();
        var client = h.AddClient(connect: false);
        client.Hooks.ThrowOnHello = _ => new InvalidDataException("Duplicate block type registry entry 1 'a'.");
        client.Connect();

        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(5)));

        Assert.Equal(DisconnectCode.ProtocolViolation, Assert.Single(client.Hooks.Disconnections).Code);
        Assert.True(h.RunUntil(() => h.ServerHooks.Left.Count == 1, TimeSpan.FromSeconds(2)));
        Assert.Equal(DisconnectCode.ProtocolViolation, h.ServerHooks.Left[0].Reason);
    }

    [Fact]
    public void ClientDisconnect_Normal_ServerReportsNormal()
    {
        using var h = new SessionHarness();
        var client = h.JoinInGame();
        var player = client.Player(h);

        client.Session.Disconnect();
        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected && h.ServerHooks.Left.Count == 1, TimeSpan.FromSeconds(5)));

        Assert.Equal((player, DisconnectCode.Normal), h.ServerHooks.Left[0]);
        Assert.Equal(DisconnectCode.Normal, Assert.Single(client.Hooks.Disconnections).Code);
    }

    [Fact]
    public void Ping_InLoading_ServerPongsAndClientMeasuresRtt()
    {
        var link = new LinkConditions(TimeSpan.FromMilliseconds(40), TimeSpan.Zero, 0, 0, 1);
        using var h = new SessionHarness(conditions: link);
        var client = h.AddClient();
        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Loading, TimeSpan.FromSeconds(5)));

        h.Run(TimeSpan.FromSeconds(3));

        Assert.True(client.Received<Pong>().Count() >= 2);
        Assert.InRange(client.Session.Rtt.TotalMilliseconds, 70, 110);
    }

    private static ConnectionId FirstServerConnection(SessionHarness h) => h.Server.ConnectionOf(h.Server.PlayersInGame[0]);
}
