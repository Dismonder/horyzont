using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests;

public sealed class ClientRuntimeTests
{
    private const ulong Build = 0x4B32_0000_0000_0020UL;

    [Fact]
    public void Listeners_GetReplyAndDisconnectInOrder()
    {
        using var rig = new Rig();
        var events = new List<string>();
        rig.Client.AddListener(new Recorder("A", events));
        rig.Client.AddListener(new Recorder("B", events));
        rig.Connect();
        rig.RunUntil(() => rig.Client.Session.State == ClientState.Loading);
        rig.Client.Session.SendReady();
        rig.RunUntil(() => rig.Hooks.Ready);

        var seq = rig.Client.Session.SubmitIntent(new RemoveBlockIntent(new EntityId(7), new CellPos(1, 2, 3)));
        rig.RunUntil(() => events.Contains($"B:reply {seq}"));
        rig.Server.Send(rig.Hooks.Player, new EntityDespawn(new EntityId(42)));
        rig.RunUntil(() => events.Contains("B:message EntityDespawn"));
        rig.Server.Shutdown(DisconnectCode.HostLeft);
        rig.RunUntil(() => rig.Client.Session.State == ClientState.Disconnected);

        Assert.Equal(
            [
                "A:hello", "B:hello",
                $"A:reply {seq}", $"B:reply {seq}",
                "A:message EntityDespawn", "B:message EntityDespawn",
                "A:disconnected HostLeft", "B:disconnected HostLeft",
            ],
            events);
        Assert.NotNull(rig.Client.Hello);
    }

    [Fact]
    public void IntentReply_ReachesOnlyOnIntentReply_NotOnMessage()
    {
        using var rig = new Rig();
        var listener = new Counting();
        rig.Client.AddListener(listener);
        rig.JoinInGame();

        rig.Client.Session.SubmitIntent(new RemoveBlockIntent(new EntityId(7), new CellPos(1, 2, 3)));
        rig.RunUntil(() => listener.Replies == 1);

        Assert.Equal(1, listener.Replies);
        Assert.Equal(0, listener.Messages);
    }

    [Fact]
    public void RemoveListener_DuringDispatch_DoesNotSkipTheNextOne()
    {
        using var rig = new Rig();
        var events = new List<string>();
        var second = new Recorder("B", events);
        SelfRemoving? first = null;
        first = new SelfRemoving(() => rig.Client.RemoveListener(first!), events);
        rig.Client.AddListener(first);
        rig.Client.AddListener(second);
        rig.JoinInGame();
        events.Clear();

        rig.Client.Session.SubmitIntent(new RemoveBlockIntent(new EntityId(7), new CellPos(1, 2, 3)));
        rig.RunUntil(() => events.Any(e => e.StartsWith("B:reply", StringComparison.Ordinal)));
        var firstRound = events.Count;
        rig.Client.Session.SubmitIntent(new RemoveBlockIntent(new EntityId(7), new CellPos(1, 2, 3)));
        rig.RunUntil(() => events.Count(e => e.StartsWith("B:reply", StringComparison.Ordinal)) == 2);

        Assert.Equal(["self-removing:reply", "B:reply 1"], events.Take(firstRound));
        Assert.Equal(1, events.Count(e => e == "self-removing:reply"));
    }

    [Fact]
    public void Rejected_GoesToTheListeners()
    {
        using var rig = new Rig();
        var listener = new Counting();
        rig.Client.AddListener(listener);

        rig.Connect(password: "wrong");
        rig.RunUntil(() => listener.Rejections.Count == 1, requireJoined: false);

        Assert.Equal([HandshakeRejectCode.BadPassword], listener.Rejections);
        Assert.Null(rig.Client.Hello);
    }

    [Fact]
    public void ListenerBug_PassesThroughPoll()
    {
        using var rig = new Rig();
        rig.JoinInGame();
        rig.Client.AddListener(new Thrower(new InvalidOperationException("listener bug (test)")));

        rig.Client.Session.SubmitIntent(new RemoveBlockIntent(new EntityId(7), new CellPos(1, 2, 3)));

        Assert.Throws<InvalidOperationException>(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                rig.Step();
            }
        });
    }

    [Fact]
    public void ListenerThrowingInvalidData_EndsTheSessionWithProtocolViolation()
    {
        using var rig = new Rig();
        var listener = new Counting();
        rig.Client.AddListener(new Thrower(new InvalidDataException("bad content (test)"), onHello: true));
        rig.Client.AddListener(listener);

        rig.Connect();
        rig.RunUntil(() => listener.Disconnections.Count == 1, requireJoined: false);

        Assert.Equal([DisconnectCode.ProtocolViolation], listener.Disconnections);
    }

    [Fact]
    public void IsHostLocal_IsWhatTheConstructorGot()
    {
        using var local = new Rig(isHostLocal: true);
        using var remote = new Rig(isHostLocal: false);

        Assert.True(local.Client.IsHostLocal);
        Assert.False(remote.Client.IsHostLocal);
    }

    [Fact]
    public void Connect_ClearsTheHelloOfThePreviousConnection()
    {
        using var rig = new Rig();
        rig.JoinInGame();
        Assert.NotNull(rig.Client.Hello);
        rig.Client.Session.Disconnect();
        rig.RunUntil(() => rig.Client.Session.State == ClientState.Disconnected, requireJoined: false);

        rig.Connect();

        Assert.Null(rig.Client.Hello);
    }

    private sealed class Rig : IDisposable
    {
        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
        private readonly InMemoryNetwork _network;
        private readonly InMemoryTransport _serverTransport;
        private readonly InMemoryTransport _clientTransport;
        private ulong _tick;

        public Rig(bool isHostLocal = true)
        {
            _network = new InMemoryNetwork(_time);
            _serverTransport = _network.CreateTransport("server", TransportOptions.Default);
            _serverTransport.Listen(new InMemoryEndpoint("server"));
            _clientTransport = _network.CreateTransport("local", TransportOptions.Default);
            Hooks = new StubServerHooks();
            Server = new ServerSession(_serverTransport, new ServerOptions { BuildHash = Build, Password = "secret" }, Hooks, _time);
            Client = new ClientRuntime(_clientTransport, new ClientOptions { BuildHash = Build }, isHostLocal, _time, NullLoggerFactory.Instance);
        }

        public StubServerHooks Hooks { get; }

        public ServerSession Server { get; }

        public ClientRuntime Client { get; }

        public void Connect(string password = "secret") =>
            Client.Connect(new InMemoryEndpoint("server"), new PlatformIdentity(IdentityPlatform.Dev, 1), "local", password);

        public void Step()
        {
            _time.Advance(TimeSpan.FromMilliseconds(10));
            Server.Poll(new Tick(++_tick));
            Client.Poll();
        }

        public void RunUntil(Func<bool> condition, bool requireJoined = true)
        {
            _ = requireJoined;
            for (var i = 0; i < 2000 && !condition(); i++)
            {
                Step();
            }

            Assert.True(condition(), "the condition did not hold in time");
        }

        public void JoinInGame()
        {
            Connect();
            RunUntil(() => Client.Session.State == ClientState.Loading);
            Client.Session.SendReady();
            RunUntil(() => Hooks.Ready);
        }

        public void Dispose()
        {
            _clientTransport.Dispose();
            _serverTransport.Dispose();
        }
    }

    private sealed class StubServerHooks : IServerGameHooks
    {
        private uint _replies;

        public PlayerId Player { get; } = new(1);

        public bool Ready { get; private set; }

        public PlayerId OnPlayerJoining(PlatformIdentity identity, string displayName) => Player;

        public ServerHello CreateHello(PlayerId player) =>
            new(new WorldId(Guid.Parse("6a1f5f2e-0000-4000-8000-000000000002")), new Tick(1), 60, player, new EntityId(100), [], [], []);

        public void OnPlayerReady(PlayerId player) => Ready = true;

        public IntentReply OnIntent(PlayerId player, uint clientSeq, Intent intent) =>
            new(clientSeq, RejectReason.None, new OpId(++_replies));

        public void OnClientMessage(PlayerId player, Message message)
        {
        }

        public void OnPlayerLeft(PlayerId player, DisconnectCode reason)
        {
        }
    }

    private sealed class Recorder(string name, List<string> events) : IClientSessionListener
    {
        public void OnServerHello(ServerHello hello) => events.Add($"{name}:hello");

        public void OnIntentReply(IntentReply reply) => events.Add($"{name}:reply {reply.ClientSeq}");

        public void OnMessage(Message message)
        {
            if (message is not Pong)
            {
                events.Add($"{name}:message {message.Type}");
            }
        }

        public void OnDisconnected(DisconnectCode code) => events.Add($"{name}:disconnected {code}");
    }

    private sealed class SelfRemoving(Action remove, List<string> events) : IClientSessionListener
    {
        public void OnIntentReply(IntentReply reply)
        {
            events.Add("self-removing:reply");
            remove();
        }
    }

    private sealed class Counting : IClientSessionListener
    {
        public int Replies { get; private set; }

        public int Messages { get; private set; }

        public List<HandshakeRejectCode> Rejections { get; } = [];

        public List<DisconnectCode> Disconnections { get; } = [];

        public void OnIntentReply(IntentReply reply) => Replies++;

        public void OnMessage(Message message)
        {
            if (message is not Pong)
            {
                Messages++;
            }
        }

        public void OnRejected(HandshakeRejectCode code, string detail) => Rejections.Add(code);

        public void OnDisconnected(DisconnectCode code) => Disconnections.Add(code);
    }

    private sealed class Thrower(Exception exception, bool onHello = false) : IClientSessionListener
    {
        public void OnServerHello(ServerHello hello)
        {
            if (onHello)
            {
                throw exception;
            }
        }

        public void OnIntentReply(IntentReply reply)
        {
            if (!onHello)
            {
                throw exception;
            }
        }
    }
}
