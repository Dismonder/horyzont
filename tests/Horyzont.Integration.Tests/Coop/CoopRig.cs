using System.Numerics;

using Horyzont.Bots.Client;
using Horyzont.Bots.Replicas;
using Horyzont.Bots.Runner;
using Horyzont.Bots.Scripting;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.World;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Persistence;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Terrain;
using Horyzont.Server;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Integration.Tests.Coop;

/// <summary>
/// A dedicated server and a crowd of bots in one process, on virtual time: the <see cref="ServerRuntime"/> over an in-memory network
/// with the fake terrain and the characters of <see cref="CoopCharacters"/>, and N <see cref="BotClient"/>s. One <see cref="Step"/>
/// is one 60 Hz frame: the server ticks first, then every bot (the order of the host). The world file survives
/// <see cref="RestartServerAsync"/>, which builds a new runtime over the same file.
/// </summary>
internal sealed class CoopRig : IAsyncDisposable
{
    public const ulong Build = 0x4B34_0000_0000_0019UL;

    /// <summary>The persistence options of every server of the rig.</summary>
    public static readonly PersistenceOptions Persistence = new() { GameVersion = "k4-19-test", BackupInterval = Timeout.InfiniteTimeSpan };

    public static readonly TimeSpan FrameTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private readonly string _dir;
    private readonly bool _ownsDir;
    private readonly List<BotClient> _bots = [];
    private readonly IPersistenceFaults _faults;
    private readonly bool _hashEveryOperation;
    private readonly InMemoryNetwork _network;
    private InMemoryTransport _serverTransport;
    private readonly Queue<(DateTimeOffset At, OpId Last)> _opHistory = new();
    private readonly bool _expectsCrash;
    private readonly bool _replicated;
    private long _frame;
    private int _spawned;
    private ServerRuntime? _server;

    public CoopRig(
        string? dir = null, LinkConditions? conditions = null, IPersistenceFaults? faults = null, bool hashEveryOperation = false,
        bool replicated = false)
    {
        _replicated = replicated;
        _ownsDir = dir is null;
        _dir = dir ?? Path.Combine(Path.GetTempPath(), "horyzont-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _faults = faults ?? NoFaults.Instance;
        _expectsCrash = faults is not null;
        _hashEveryOperation = hashEveryOperation;
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        _network = new InMemoryNetwork(Time, conditions);
        _serverTransport = _network.CreateTransport("server", TransportOptions.Default);
        (Server, Characters, Terrain) = NewServer();
    }

    public FakeTimeProvider Time { get; }

    public ServerRuntime Server { get; private set; }

    public CoopCharacters Characters { get; private set; }

    public FakeServerTerrainFactory Terrain { get; private set; }

    /// <summary>Gets the entity and terrain replication of the server (phase B rigs); null otherwise.</summary>
    public CoopReplication? Replication { get; private set; }

    public IReadOnlyList<BotClient> Bots => _bots;

    /// <summary>Gets the state hash after each operation of the current server run (when <c>hashEveryOperation</c> was asked for).</summary>
    public CoopHashingSink? Hashes { get; private set; }

    public string DirectoryPath => _dir;

    public string WorldPath => Path.Combine(_dir, "saves", "world.db");

    /// <summary>Gets the crowd that steps the bots when <see cref="UseCrowd"/> was called; null when the rig steps them itself.</summary>
    public BotCrowd? Crowd { get; private set; }

    /// <summary>Lets a <see cref="BotCrowd"/> on the virtual clock step the bots, as the runner process does on the system clock.</summary>
    /// <returns>The crowd, whose epoch is the current virtual time.</returns>
    public BotCrowd UseCrowd() => Crowd = new BotCrowd(_bots, Time, Time.GetUtcNow());

    /// <summary>Gets the frame counter, which is also the script tick of the bots.</summary>
    public long Frame => _frame;

    public ulong Hash => StateHasher.Hash(Server.State);

    public async Task StartServerAsync()
    {
        var result = await Server.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(result.IsRunning, "the server did not start: " + result);
        Characters.Bind(Server);
    }

    /// <summary>Adds a bot that connects on its first step.</summary>
    public BotClient AddBot(BotRole role, ulong seed, BotSchedule? schedule = null, TimeSpan? reconnectDelay = null)
    {
        var index = _bots.Count;
        var transport = _network.CreateTransport("bot" + index, TransportOptions.Default);
        var bot = new BotClient(
            new BotClientOptions
            {
                Name = "bot" + index,
                Identity = new PlatformIdentity(IdentityPlatform.Dev, 5000UL + (ulong)index),
                Server = new InMemoryEndpoint("server"),
                BuildHash = Build,
                Seed = seed,
                Role = role,
                Schedule = schedule ?? new BotSchedule(),
                ReconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(2),
            },
            transport, Time,
            _replicated ? new BotReplicaSet(new ConstructMirrorReplica(Time), new TerrainReplica(Path.Combine(_dir, "bots", "bot" + index), Time)) : null);
        _bots.Add(bot);
        return bot;
    }

    /// <summary>One frame of 1/60 s: the server ticks, then every bot.</summary>
    public void Step()
    {
        Time.Advance(FrameTime);
        Server.Tick();
        if (Crowd is { } crowd)
        {
            crowd.Pump();
        }
        else
        {
            foreach (var bot in _bots)
            {
                bot.Tick(_frame);
            }
        }

        _frame++;
        _opHistory.Enqueue((Time.GetUtcNow(), Server.State.LastOpId));
    }

    /// <summary>
    /// Waits (real time) until the writer thread has made durable every operation that is older than one flush interval of virtual
    /// time, which is what it promises; it replaces a blind sleep that only hoped the thread had run. Without injected faults a
    /// writer that does not catch up within a few seconds fails the test.
    /// </summary>
    public async Task DrainWriterAsync()
    {
        var due = Time.GetUtcNow() - Persistence.FlushInterval - FrameTime;
        var target = OpId.None;
        while (_opHistory.Count > 1 && _opHistory.Peek().At < due - TimeSpan.FromSeconds(5))
        {
            _opHistory.Dequeue();
        }

        foreach (var (at, last) in _opHistory)
        {
            if (at <= due)
            {
                target = last;
            }
        }

        var limit = Environment.TickCount64 + 5000;
        while (Server.DurableOpId < target && Environment.TickCount64 < limit)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        if (!_expectsCrash)
        {
            Assert.True(Server.DurableOpId >= target, $"the writer is at operation {Server.DurableOpId.Value}, expected {target.Value}");
        }
    }

    /// <summary>Steps frames; every few seconds of virtual time the writer thread is given the chance to catch up.</summary>
    public async Task RunAsync(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            Step();
            if (i % 300 == 299)
            {
                await DrainWriterAsync();
            }
        }
    }

    public async Task<bool> RunUntilAsync(Func<bool> condition, int maxFrames = 20_000)
    {
        for (var i = 0; i < maxFrames; i++)
        {
            if (condition())
            {
                return true;
            }

            Step();
            if (i % 50 == 49)
            {
                await DrainWriterAsync();
            }
        }

        return condition();
    }

    /// <summary>Asks for the shutdown and steps frames until the runtime has stopped (the bots keep running and notice the end).</summary>
    public async Task StopServerAsync()
    {
        Server.RequestShutdown();
        Assert.True(
            await RunUntilAsync(() => Server.IsStopped, 100_000),
            "the server did not stop; steps done: " + string.Join(", ", Server.CompletedShutdownSteps));
    }

    /// <summary>Disposes the stopped server and builds a new one over the same world file; the bots reconnect by themselves.</summary>
    public async Task RestartServerAsync()
    {
        Assert.True(Server.IsStopped);
        CloseTerrain();
        _serverTransport.Dispose();
        _serverTransport = _network.CreateTransport("server", TransportOptions.Default);
        (Server, Characters, Terrain) = NewServer();
        await StartServerAsync();
    }

    public ValueTask DisposeAsync()
    {
        foreach (var bot in _bots)
        {
            bot.Runtime.Session.Disconnect();
            (bot.Replica as IDisposable)?.Dispose();
        }

        _serverTransport.Dispose();
        CloseTerrain();
        if (_ownsDir)
        {
            new TerrainBlockStore(WorldPath).Close();
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
                // Safe to leave: the directory is unique and under the temp directory.
            }
        }

        return ValueTask.CompletedTask;
    }

    private (ServerRuntime, CoopCharacters, FakeServerTerrainFactory) NewServer()
    {
        var terrain = new FakeServerTerrainFactory(Time);
        var characters = new CoopCharacters();
        var options = new ServerRuntimeOptions
        {
            WorldPath = WorldPath,
            BackupsDirectory = Path.Combine(_dir, "saves", "backups"),
            ServerOptions = new ServerOptions { BuildHash = Build },
            PersistenceOptions = Persistence,
            Listen = [new InMemoryEndpoint("server")],
            Faults = _faults,
            BlockTypeKeys = [WorldFactory.StoneBlockKey],
            MaterialKeys = ["stone", "dirt"],
            NewPlayerSpawn = SpawnOnEquator,
            JournalSinkDecorator = _hashEveryOperation ? inner => Hashes = new CoopHashingSink(() => _server!.State, inner) : null,
        };
        var runtime = new ServerRuntime(
            ProcessMode.DedicatedServer, options, _serverTransport, terrain, WorldFactory.CreateDefault, Time, NullLoggerFactory.Instance);
        _server = runtime;
        runtime.AddClientMessageHandler(characters);
        runtime.AddTickParticipant(characters);
        runtime.AddPlayerObserver(characters);
        Replication = null;
        if (_replicated)
        {
            // Registered after the characters, as in the game: the movement of a tick runs before its snapshots are built.
            characters.SendsOwnSnapshots = false;
            Replication = new CoopReplication(characters, Time);
            Replication.Register(runtime);
        }

        return (runtime, characters, terrain);
    }

    /// <summary>The game spawns on the equator (the pole is a degenerate frame); the players stand on a ring of 3 m around the point.</summary>
    private (Vector3 Feet, Quaternion BodyRotation) SpawnOnEquator(PlanetParams planet)
    {
        var angle = _spawned++ * (MathF.PI / 4f);
        var feet = new Vector3(planet.RadiusMeters + 0.1f, 3f * MathF.Sin(angle), 3f * MathF.Cos(angle));
        var up = Vector3.Normalize(feet);
        return (Vector3.Normalize(feet) * (planet.RadiusMeters + 0.1f), SurfaceFrame.FromYaw(up, 0f));
    }

    private void CloseTerrain()
    {
        foreach (var terrain in Terrain.Created)
        {
            terrain.Dispose();
        }
    }
}
