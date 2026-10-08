using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Constructs;
using Horyzont.Net.Session;
using Horyzont.Net.Tests.Transport;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Net.Tests.Session;

/// <summary>One server session and any number of clients on an in-memory network driven by a fake clock.</summary>
public sealed class SessionHarness : IDisposable
{
    public const ulong Build = 0xB0B0_1234_5678_9ABCUL;

    public static readonly TimeSpan StepSize = TimeSpan.FromMilliseconds(10);

    private readonly List<TestClient> _clients = [];
    private readonly List<RawPeer> _raw = [];
    private ulong _tick;

    public SessionHarness(ServerOptions? options = null, LinkConditions? conditions = null, TransportOptions? transportOptions = null)
    {
        Network = new InMemoryNetwork(Time, conditions);
        TransportOptions = transportOptions ?? TransportOptions.Default;
        ServerTransport = Network.CreateTransport("server", TransportOptions);
        ServerTransport.Listen(new InMemoryEndpoint("server"));
        Options = options ?? new ServerOptions { BuildHash = Build };
        Server = new ServerSession(ServerTransport, Options, ServerHooks, Time, Logger);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));

    public InMemoryNetwork Network { get; }

    public TransportOptions TransportOptions { get; }

    public InMemoryTransport ServerTransport { get; }

    public ServerOptions Options { get; }

    public ServerSession Server { get; }

    public RecordingServerHooks ServerHooks { get; } = new();

    public RecordingLogger<ServerSession> Logger { get; } = new();

    public IReadOnlyList<TestClient> Clients => _clients;

    public bool PollServer { get; set; } = true;

    public static ServerHello HelloFor(PlayerId player) =>
        new(new WorldId(Guid.Parse("6a1f5f2e-0000-4000-8000-000000000001")), new Tick(1), 30, player, new EntityId(player.Value + 100), [], [], []);

    public static PlatformIdentity DevIdentity(ulong value) => new(IdentityPlatform.Dev, value);

    public static Intent SampleIntent() => new RemoveBlockIntent(new EntityId(7), new CellPos(1, 2, 3));

    public static byte[] Encode(Message message)
    {
        var writer = new ByteWriter();
        MessageCodec.Write(writer, message);
        return writer.WrittenSpan.ToArray();
    }

    public TestClient AddClient(
        PlatformIdentity? identity = null, ulong? buildHash = null, string password = "", ulong? fakeSteamId = null, bool connect = true)
    {
        var name = "client" + (_clients.Count + _raw.Count + 1);
        var transport = Network.CreateTransport(name, TransportOptions, fakeSteamId);
        var hooks = new RecordingClientHooks();
        var session = new ClientSession(transport, new ClientOptions { BuildHash = buildHash ?? Build }, hooks, Time);
        hooks.Session = session;
        var client = new TestClient(name, transport, session, hooks, identity ?? DevIdentity((ulong)(_clients.Count + 1000)), password);
        _clients.Add(client);
        if (connect)
        {
            client.Connect();
        }

        return client;
    }

    public RawPeer AddRaw()
    {
        var name = "raw" + (_clients.Count + _raw.Count + 1);
        var transport = Network.CreateTransport(name, TransportOptions);
        var peer = new RawPeer(transport);
        _raw.Add(peer);
        peer.Connection = transport.Connect(new InMemoryEndpoint("server"));
        return peer;
    }

    /// <summary>Connects a client and drives it to InGame.</summary>
    public TestClient JoinInGame(PlatformIdentity? identity = null)
    {
        var client = AddClient(identity);
        Assert.True(RunUntil(() => client.Session.State == ClientState.Loading, TimeSpan.FromSeconds(5)), "client did not reach Loading");
        client.Session.SendReady();
        var player = client.Player(this);
        Assert.True(RunUntil(() => ServerHooks.Ready.Contains(player), TimeSpan.FromSeconds(5)), "server did not see ClientReady");
        return client;
    }

    public void Step() => Step(StepSize);

    public void Step(TimeSpan dt)
    {
        Time.Advance(dt);
        PollAll();
    }

    public void PollAll()
    {
        foreach (var client in _clients)
        {
            if (client.Polling)
            {
                client.Session.Poll();
            }
            else if (client.PumpTransportOnly)
            {
                client.Transport.Poll(client.Sink);
            }
        }

        foreach (var raw in _raw)
        {
            raw.Transport.Poll(raw.Events);
        }

        if (PollServer)
        {
            Server.Poll(new Tick(++_tick));
        }
    }

    public bool RunUntil(Func<bool> condition, TimeSpan max)
    {
        var elapsed = TimeSpan.Zero;
        while (!condition())
        {
            if (elapsed >= max)
            {
                return false;
            }

            Step();
            elapsed += StepSize;
        }

        return true;
    }

    public void Run(TimeSpan duration) => RunUntil(() => false, duration);

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Transport.Dispose();
        }

        foreach (var raw in _raw)
        {
            raw.Transport.Dispose();
        }

        ServerTransport.Dispose();
    }
}

public sealed class TestClient(
    string name, InMemoryTransport transport, ClientSession session, RecordingClientHooks hooks, PlatformIdentity identity, string password)
{
    public string Name { get; } = name;

    public InMemoryTransport Transport { get; } = transport;

    public ClientSession Session { get; } = session;

    public RecordingClientHooks Hooks { get; } = hooks;

    public PlatformIdentity Identity { get; } = identity;

    /// <summary>When false the session is not polled (silence).</summary>
    public bool Polling { get; set; } = true;

    /// <summary>When <see cref="Polling"/> is false, still poll the bare transport so keep-alives flow.</summary>
    public bool PumpTransportOnly { get; set; }

    public RecordingEvents Sink { get; } = new();

    public void Connect() => Session.Connect(new InMemoryEndpoint("server"), Identity, Name, password);

    public PlayerId Player(SessionHarness harness) => harness.ServerHooks.PlayerOf(Identity);

    public IEnumerable<T> Received<T>()
        where T : Message => Hooks.Messages.OfType<T>();
}

/// <summary>A bare transport that speaks raw frames to the server.</summary>
public sealed class RawPeer(InMemoryTransport transport)
{
    public InMemoryTransport Transport { get; } = transport;

    public RecordingEvents Events { get; } = new();

    public ConnectionId Connection { get; set; }

    public bool IsConnected => Events.Connected.Contains(Connection);

    public bool IsClosed => Events.Disconnected.Exists(d => d.Connection == Connection);

    public void Send(TransportChannel channel, byte[] frame) => Transport.Send(Connection, channel, frame);

    public IEnumerable<Message> Messages() => Events.Received.Select(m => MessageCodec.Read(m.Payload));
}
