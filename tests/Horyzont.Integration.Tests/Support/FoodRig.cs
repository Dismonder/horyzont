using System.Diagnostics;
using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Persistence;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Terrain;
using Horyzont.Server;
using Horyzont.Server.Crafting;
using Horyzont.Server.Inventory;
using Horyzont.Server.Survival;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Integration.Tests.Support;

/// <summary>
/// A solo server with the survival rules (food test content), the inventory, the progress replication and the vitals service over an
/// in-memory network and one client. Several rigs may share a world directory one after another to restart the server. With <c>hazards</c> the content is
/// <see cref="SurvivalContentFactory.CreateTestWithHazards"/> (K6-34: the suit, the modules and the oxygen generator; the start kit also hands out the refills, the modules and the fuel) and the home planet
/// has the profile given.
/// </summary>
internal sealed class FoodRig : IAsyncDisposable
{
    public const ulong Build = 0x4B36_3300_0000_0001UL;

    private static readonly TimeSpan _frame = TimeSpan.FromMilliseconds(16);

    private readonly InMemoryNetwork _network;
    private readonly InMemoryTransport _serverTransport;
    private readonly List<InMemoryTransport> _clientTransports = [];

    public FoodRig(string directory, ArmedFaults? faults = null, bool hazards = false, string homeProfile = "core:temperate")
    {
        Directory = directory;
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 18, 0, 0, TimeSpan.Zero));
        Terrain = new FakeServerTerrainFactory(Time);
        _network = new InMemoryNetwork(Time);
        _serverTransport = _network.CreateTransport("server", TransportOptions.Default);
        var options = new ServerRuntimeOptions
        {
            WorldPath = WorldPath(directory),
            ServerOptions = new ServerOptions { BuildHash = Build },
            PersistenceOptions = new PersistenceOptions { GameVersion = "k6-33-test", BackupInterval = Timeout.InfiniteTimeSpan },
            Listen = [new InMemoryEndpoint("server")],
            LocalClientEndpoint = new InMemoryEndpoint("local"),
            BlockTypeKeys = hazards ? HazardBlocks.BlockKeys : [WorldFactory.StoneBlockKey],
            BlockDefinitions = hazards ? HazardBlocks.DefinitionsFor() : new Dictionary<string, BlockTypeDef>(),
            MaterialKeys = ["stone", "dirt"],
            Content = hazards ? SurvivalContentFactory.CreateTestWithHazards(homeProfile) : SurvivalContentFactory.CreateTestWithFood(),
            ItemTypeKeys = hazards ? SurvivalContentFactory.HazardItemKeys : SurvivalContentFactory.FoodItemKeys,
            Faults = (IPersistenceFaults?)faults ?? Horyzont.Persistence.Faults.NoFaults.Instance,
        };
        Runtime = new ServerRuntime(ProcessMode.Solo, options, _serverTransport, Terrain, hazards ? NewHazardWorld : NewWorld, Time, NullLoggerFactory.Instance);
        var inventory = InventoryService.Register(Runtime, NullLoggerFactory.Instance);
        if (hazards)
        {
            StationService.Register(Runtime, inventory, NullLoggerFactory.Instance);
        }

        ProgressReplication.Register(Runtime);
        Vitals = VitalsService.Register(Runtime, Motion, NullLoggerFactory.Instance);
    }

    public string Directory { get; }

    public FakeTimeProvider Time { get; }

    public FakeServerTerrainFactory Terrain { get; }

    public ServerRuntime Runtime { get; }

    /// <summary>The motion of the bodies, set by the test (the rig has no characters).</summary>
    public FakeMotion Motion { get; } = new();

    public VitalsService Vitals { get; private set; } = null!;

    public ClientSession? Client { get; private set; }

    public RecordingClientHooks? Hooks { get; private set; }

    public ContainerMirror Mirror { get; } = new();

    public ulong Hash => StateHasher.Hash(Runtime.State);

    public static string WorldPath(string directory) => Path.Combine(directory, "saves", "world.db");

    public static WorldState NewWorld() => WorldState.CreateNew(
        WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], [WorldFactory.StoneBlockKey], ["stone", "dirt"],
        SurvivalContentFactory.FoodItemKeys);

    /// <summary>A fresh world for the rig with hazards: the block types of <see cref="HazardBlocks"/> and the item types of the hazard content.</summary>
    public static WorldState NewHazardWorld() => WorldState.CreateNew(
        WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], HazardBlocks.BlockKeys, ["stone", "dirt"], SurvivalContentFactory.HazardItemKeys);

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
    public async Task<PlayerId> JoinAsync(ulong identity, string? endpointName = null)
    {
        var transport = _network.CreateTransport(endpointName ?? $"client{identity}", TransportOptions.Default);
        _clientTransports.Add(transport);
        Hooks = new RecordingClientHooks();
        Client = new ClientSession(transport, new ClientOptions { BuildHash = Build }, Hooks, Time);
        Client.Connect(new InMemoryEndpoint("server"), new PlatformIdentity(IdentityPlatform.Dev, identity), "player");
        Assert.True(await RunUntilAsync(() => Client.State == ClientState.Loading), "client did not reach Loading");
        Client.SendReady();
        Assert.True(await RunUntilAsync(() => Client.State == ClientState.InGame), "client did not reach InGame");
        Assert.True(await RunUntilAsync(() => Mirror.Ids.Count == 3), "the container states did not arrive");
        var player = Hooks.Hello!.YourPlayer;
        Motion.Set(player, MovementMode.Walking);
        return player;
    }

    public async Task<IntentReply> SubmitAsync(Intent intent)
    {
        var replies = Hooks!.Replies.Count;
        Client!.SubmitIntent(intent);
        Assert.True(await RunUntilAsync(() => Hooks.Replies.Count > replies), "no intent reply");
        StepMany(2);
        return Hooks.Replies[^1];
    }

    /// <summary>The newest message of a type the client received.</summary>
    public T? Latest<T>()
        where T : Message => Hooks!.Messages.OfType<T>().LastOrDefault();

    /// <summary>Runs frames until the client has received a message of a type that satisfies <paramref name="match"/>.</summary>
    public Task<bool> WaitForAsync<T>(Func<T, bool> match, int maxFrames = 3000)
        where T : Message => RunUntilAsync(() => Hooks!.Messages.OfType<T>().Any(match), maxFrames);

    /// <summary>Disconnects the client, waits until nobody is online and stops the server.</summary>
    public async Task DisconnectAndStopAsync()
    {
        Client!.Disconnect();
        Assert.True(await RunUntilAsync(() => !Runtime.State.Players.Online.Any()), "the player did not leave");
        await StopAsync();
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

    /// <summary>The motion of the bodies of the rig: a set mode and velocity on the pole of the planet (up is +y).</summary>
    public sealed class FakeMotion : IVitalsMotionSource
    {
        private readonly Dictionary<PlayerId, VitalsMotion> _motion = [];

        public void Set(PlayerId player, MovementMode mode, Vector3 velocity = default) =>
            _motion[player] = new VitalsMotion(mode, velocity, new Vector3(0f, WorldFactory.HomePlanetParams.RadiusMeters + 5f, 0f), false);

        public bool TryGetMotion(PlayerId player, out VitalsMotion motion) => _motion.TryGetValue(player, out motion);
    }
}
