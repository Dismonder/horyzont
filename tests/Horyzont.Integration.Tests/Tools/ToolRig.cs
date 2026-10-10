using System.Numerics;

using Horyzont.Client.Session;
using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Integration.Tests.Support;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Integration.Tests.Tools;

/// <summary>
/// A server (simulation over a fake terrain sphere, server session) and clients (client runtime, tool controller, pending tracker)
/// joined by the in-memory transport. One call of Pump is one physics tick of everybody at 60 Hz.
/// </summary>
internal sealed class ToolRig : IDisposable
{
    public const ulong Build = 0x4B32_0D00_0000_0001UL;

    private static readonly TimeSpan _step = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private readonly List<ToolClient> _clients = [];
    private readonly InMemoryTransport _serverTransport;
    private ulong _tick;

    public ToolRig(float surfaceRadius = 4000f, IJournalSink? journal = null, WorldState? state = null, LinkConditions? link = null, FakeTimeProvider? time = null)
    {
        Time = time ?? new FakeTimeProvider(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
        Terrain = new FakeTerrainService(surfaceRadius);
        Journal = new InMemoryJournal();
        Sim = new ServerSimulation(state ?? WorldFactory.CreateDefault(), Terrain, journal ?? Journal, new GameRules(), NullLogger<ServerSimulation>.Instance);
        Hooks = new SimulationServerHooks(Sim);
        Network = new InMemoryNetwork(Time, link);
        _serverTransport = Network.CreateTransport("server", TransportOptions.Default);
        _serverTransport.Listen(new InMemoryEndpoint("server"));
        Server = new ServerSession(_serverTransport, new ServerOptions { BuildHash = Build }, Hooks, Time);
    }

    public FakeTimeProvider Time { get; }

    public FakeTerrainService Terrain { get; }

    public InMemoryJournal Journal { get; }

    public ServerSimulation Sim { get; }

    public SimulationServerHooks Hooks { get; }

    public InMemoryNetwork Network { get; }

    public ServerSession Server { get; }

    public GameRules Rules => Sim.Rules;

    /// <summary>Server tick == client tick: every actor ticks once per pump.</summary>
    public long Tick => (long)_tick;

    public Vector3 Ground(float x, float z = 0f)
    {
        var r = Terrain.SurfaceRadius;
        return new Vector3(x, MathF.Sqrt((r * r) - (x * x) - (z * z)), z);
    }

    public ToolClient AddClient(string name, ulong identity)
    {
        var client = new ToolClient(this, name, new PlatformIdentity(IdentityPlatform.Dev, identity));
        _clients.Add(client);
        client.Connect();
        Assert.True(RunUntil(() => client.Runtime.Session.State == ClientState.Loading), "the client did not reach Loading");
        client.Runtime.Session.SendReady();
        Assert.True(RunUntil(() => client.Runtime.Session.State == ClientState.InGame), "the client did not reach InGame");
        return client;
    }

    public void Pump()
    {
        Time.Advance(_step);
        foreach (var client in _clients)
        {
            client.Runtime.Poll();
        }

        Server.Poll(new Tick(++_tick));
        Sim.AdvanceTick();
    }

    public void Pump(int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            Pump();
        }
    }

    public bool RunUntil(Func<bool> condition, int maxTicks = 2000)
    {
        for (var i = 0; i < maxTicks && !condition(); i++)
        {
            Pump();
        }

        return condition();
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        _serverTransport.Dispose();
    }

    /// <summary>One local client: runtime, session listener, pending tracker and tool controller, wired like the game scene.</summary>
    internal sealed class ToolClient : IClientSessionListener, IDisposable
    {
        private readonly ToolRig _rig;
        private readonly string _name;
        private readonly PlatformIdentity _identity;
        private readonly InMemoryTransport _transport;

        public ToolClient(ToolRig rig, string name, PlatformIdentity identity)
        {
            _rig = rig;
            _name = name;
            _identity = identity;
            _transport = rig.Network.CreateTransport(name, TransportOptions.Default);
            Runtime = new ClientRuntime(_transport, new ClientOptions { BuildHash = Build }, isHostLocal: true, rig.Time, NullLoggerFactory.Instance);
            Runtime.AddListener(this);
            Sender = new SessionIntentSender(Runtime.Session);
            Tracker = new PendingIntentTracker(Prediction, NullLogger<PendingIntentTracker>.Instance);
        }

        public ClientRuntime Runtime { get; }

        public SessionIntentSender Sender { get; }

        public RecordingPrediction Prediction { get; } = new();

        public PendingIntentTracker Tracker { get; }

        public List<IntentReply> Replies { get; } = [];

        public ToolController Controller { get; private set; } = null!;

        public PlayerId Player { get; private set; }

        public PlayerState State => _rig.Sim.State.Players.TryGet(Player, out var player) ? player : throw new InvalidOperationException();

        public Vector3 Feet => State.Position;

        public void Connect() => Runtime.Connect(new InMemoryEndpoint("server"), _identity, _name);

        public void OnServerHello(ServerHello hello)
        {
            Player = hello.YourPlayer;
            Controller = new ToolController(
                _rig.Rules, new StateToolView(_rig.Sim.State, _rig.Terrain, Player), Sender, Tracker, BlockPalette.FromHello(hello),
                MaterialPalette.FromHello(hello), NullLogger<ToolController>.Instance);
        }

        public void OnIntentReply(IntentReply reply)
        {
            Replies.Add(reply);
            Tracker.OnReply(reply, out _);
        }

        public void OnDisconnected(DisconnectCode code) => Tracker.Clear();

        /// <summary>First person aim at a point, like the game does after the client physics tick.</summary>
        public AimRay AimAt(Vector3 target)
        {
            var eye = AimRayBuilder.EyeFromFeet(Feet, _rig.Rules);
            Assert.True(AimRayBuilder.TryBuild(
                FakeToolWorldViewPlanet, Feet, eye, target - eye, -Vector3.UnitZ, new StateToolView(_rig.Sim.State, _rig.Terrain, Player), _rig.Rules, out var ray));
            return ray;
        }

        public Vector3 Eye => AimRayBuilder.EyeFromFeet(Feet, _rig.Rules);

        public uint SubmitRaw(Intent intent) => Runtime.Session.SubmitIntent(intent);

        public void Update(in ToolInputFrame input, in AimRay aim) => Controller.Update(input, aim, _rig.Tick);

        public void Dispose() => _transport.Dispose();

        private static PlanetId FakeToolWorldViewPlanet => WorldFactory.HomePlanet;
    }

    internal sealed class RecordingPrediction : IEditPrediction
    {
        public List<string> Calls { get; } = [];

        public void Begin(uint clientSeq, Intent intent, in ToolPreview preview) => Calls.Add($"begin {clientSeq}");

        public void Commit(uint clientSeq) => Calls.Add($"commit {clientSeq}");

        public void Revert(uint clientSeq) => Calls.Add($"revert {clientSeq}");
    }
}
