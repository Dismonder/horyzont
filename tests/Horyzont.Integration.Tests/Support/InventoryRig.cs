using System.Diagnostics;
using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Persistence;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Terrain;
using Horyzont.Server;
using Horyzont.Server.Inventory;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Integration.Tests.Support;

/// <summary>
/// A solo server with the survival rules (test content) over an in-memory network and one client. Several rigs may share a
/// world directory one after another to restart the server.
/// </summary>
internal sealed class InventoryRig : IAsyncDisposable
{
    public const ulong Build = 0x4B36_2900_0000_0001UL;

    private static readonly TimeSpan _frame = TimeSpan.FromMilliseconds(16);

    private readonly InMemoryNetwork _network;
    private readonly InMemoryTransport _serverTransport;
    private readonly List<InMemoryTransport> _clientTransports = [];

    public InventoryRig(string directory, ArmedFaults? faults = null)
    {
        Directory = directory;
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        Terrain = new FakeServerTerrainFactory(Time);
        _network = new InMemoryNetwork(Time);
        _serverTransport = _network.CreateTransport("server", TransportOptions.Default);
        var options = new ServerRuntimeOptions
        {
            WorldPath = WorldPath(directory),
            ServerOptions = new ServerOptions { BuildHash = Build },
            PersistenceOptions = new PersistenceOptions { GameVersion = "k6-29-test", BackupInterval = Timeout.InfiniteTimeSpan },
            Listen = [new InMemoryEndpoint("server")],
            LocalClientEndpoint = new InMemoryEndpoint("local"),
            BlockTypeKeys = [WorldFactory.StoneBlockKey],
            MaterialKeys = ["stone", "dirt"],
            Content = SurvivalContentFactory.CreateTest(),
            ItemTypeKeys = SurvivalContentFactory.ItemKeys,
            Faults = (IPersistenceFaults?)faults ?? Horyzont.Persistence.Faults.NoFaults.Instance,
        };
        Runtime = new ServerRuntime(ProcessMode.Solo, options, _serverTransport, Terrain, NewWorld, Time, NullLoggerFactory.Instance);
        InventoryService.Register(Runtime, NullLoggerFactory.Instance);
    }

    public string Directory { get; }

    public FakeTimeProvider Time { get; }

    public FakeServerTerrainFactory Terrain { get; }

    public ServerRuntime Runtime { get; }

    public ClientSession? Client { get; private set; }

    public RecordingClientHooks? Hooks { get; private set; }

    public ContainerMirror Mirror { get; } = new();

    public ulong Hash => StateHasher.Hash(Runtime.State);

    public static string WorldPath(string directory) => Path.Combine(directory, "saves", "world.db");

    public static WorldState NewWorld() => WorldState.CreateNew(
        WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], [WorldFactory.StoneBlockKey], ["stone", "dirt"],
        SurvivalContentFactory.ItemKeys);

    public async Task StartAsync() =>
        Assert.IsType<WorldStartResult.Started>(await Runtime.StartAsync(TestContext.Current.CancellationToken));

    public void Step()
    {
        Time.Advance(_frame);
        Runtime.Tick();
        Client?.Poll();
        if (Hooks is not null)
        {
            Mirror.Apply(Hooks.Messages);
        }
    }

    public async Task<bool> RunUntilAsync(Func<bool> condition, int maxFrames = 100_000)
    {
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < maxFrames && watch.Elapsed < TimeSpan.FromSeconds(30); i++)
        {
            if (condition())
            {
                return true;
            }

            Step();
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        return condition();
    }

    public void StepMany(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            Step();
        }
    }

    /// <summary>Connects the client, sends ready and waits until the item registry and the three container states arrived.</summary>
    public async Task<PlayerId> JoinAsync(ulong identity)
    {
        var transport = _network.CreateTransport($"client{identity}", TransportOptions.Default);
        _clientTransports.Add(transport);
        Hooks = new RecordingClientHooks();
        Client = new ClientSession(transport, new ClientOptions { BuildHash = Build }, Hooks, Time);
        Client.Connect(new InMemoryEndpoint("server"), new PlatformIdentity(IdentityPlatform.Dev, identity), "player");
        Assert.True(await RunUntilAsync(() => Client.State == ClientState.Loading), "client did not reach Loading");
        Client.SendReady();
        Assert.True(await RunUntilAsync(() => Client.State == ClientState.InGame), "client did not reach InGame");
        Assert.True(await RunUntilAsync(() => Mirror.Ids.Count == 3), "the container states did not arrive");
        return Hooks.Hello!.YourPlayer;
    }

    public async Task<IntentReply> SubmitAsync(Intent intent)
    {
        var replies = Hooks!.Replies.Count;
        Client!.SubmitIntent(intent);
        Assert.True(await RunUntilAsync(() => Hooks.Replies.Count > replies), "no intent reply");
        StepMany(2);
        return Hooks.Replies[^1];
    }

    public TerrainToolIntent DigIntent(PlayerId player, float x, float z)
    {
        Runtime.State.Players.TryGet(player, out var p);
        var eye = p!.EyePosition(Runtime.Simulation.Rules);
        var r = Terrain.Last.Terrain.SurfaceRadius;
        var ground = new Vector3(x, MathF.Sqrt((r * r) - (x * x) - (z * z)), z);
        return new TerrainToolIntent(WorldFactory.HomePlanet, eye, Vector3.Normalize(ground - eye), 1f, TerrainEditMode.Remove, 0);
    }

    public async Task StopAsync()
    {
        Runtime.RequestShutdown();
        Assert.True(
            await RunUntilAsync(() => Runtime.IsStopped), "the server did not stop; steps done: " + string.Join(", ", Runtime.CompletedShutdownSteps));
    }

    public ValueTask DisposeAsync()
    {
        foreach (var transport in _clientTransports)
        {
            transport.Dispose();
        }

        _serverTransport.Dispose();
        foreach (var terrain in Terrain.Created)
        {
            terrain.Dispose();
        }

        new TerrainBlockStore(WorldPath(Directory)).Close();
        return ValueTask.CompletedTask;
    }
}
