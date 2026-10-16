using System.Diagnostics;
using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Props;
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
using Horyzont.Server.Props;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Integration.Tests.Support;

/// <summary>What a client knows of the harvested props, built only from <see cref="PropStates"/> messages (a region is replaced as a whole).</summary>
internal sealed class PropMirror
{
    private readonly Dictionary<PropRegionKey, Dictionary<PropId, Tick>> _regions = [];
    private int _consumed;

    /// <summary>Applies the messages recorded since the last call.</summary>
    public void Apply(IReadOnlyList<Message> messages)
    {
        for (; _consumed < messages.Count; _consumed++)
        {
            if (messages[_consumed] is PropStates states)
            {
                foreach (var region in states.Regions)
                {
                    _regions[region.Region] = region.Props.ToDictionary(e => e.Prop, e => e.Tick);
                }
            }
        }
    }

    public bool HasRegion(PropRegionKey region) => _regions.ContainsKey(region);

    public bool IsHarvested(PropId prop) => _regions.TryGetValue(prop.Region, out var region) && region.ContainsKey(prop);

    public Tick TickOf(PropId prop) => _regions[prop.Region][prop];

    /// <summary>Every harvested prop the client knows, ascending by identifier.</summary>
    public List<(PropId Prop, Tick Tick)> Snapshot() =>
        [.. _regions.Values.SelectMany(r => r.Select(p => (p.Key, p.Value))).OrderBy(p => p.Key.Value)];
}

/// <summary>A client of the props rig with everything the server told it.</summary>
internal sealed class PropsPlayer(ClientSession client, RecordingClientHooks hooks)
{
    public ClientSession Client { get; } = client;

    public RecordingClientHooks Hooks { get; } = hooks;

    public ContainerMirror Containers { get; } = new();

    public PropMirror Props { get; } = new();

    public PlayerId Player => Hooks.Hello!.YourPlayer;
}

/// <summary>
/// A solo server with the survival rules, the test content of the props, the inventory service, the regrowth of flora and the replication of
/// the harvested props over an in-memory network and any number of clients. The planet is the world of <see cref="WorldFactory.CreateWithProps"/>
/// over a flat fake terrain; several rigs may share a world directory one after another to restart the server.
/// </summary>
internal sealed class PropsRig : IAsyncDisposable
{
    public const ulong Build = 0x4B36_3100_0000_0001UL;

    /// <summary>Height of the surface over the planet radius, metres.</summary>
    public const float Height = 5f;

    private static readonly TimeSpan _frame = TimeSpan.FromMilliseconds(16);

    private readonly InMemoryNetwork _network;
    private readonly InMemoryTransport _serverTransport;
    private readonly List<InMemoryTransport> _clientTransports = [];
    private readonly List<PropsPlayer> _players = [];

    public PropsRig(string directory, ArmedFaults? faults = null)
    {
        Directory = directory;
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 15, 0, 0, TimeSpan.Zero));
        Terrain = new FakeServerTerrainFactory(Time)
        {
            OnCreated = created =>
            {
                created.Terrain.SurfaceRadius = Surface;
                created.Terrain.GeneratedSurfaceRadius = Surface;
            },
        };
        _network = new InMemoryNetwork(Time);
        _serverTransport = _network.CreateTransport("server", TransportOptions.Default);
        var options = new ServerRuntimeOptions
        {
            WorldPath = WorldPath(directory),
            ServerOptions = new ServerOptions { BuildHash = Build },
            PersistenceOptions = new PersistenceOptions { GameVersion = "k6-31-test", BackupInterval = Timeout.InfiniteTimeSpan },
            Listen = [new InMemoryEndpoint("server")],
            LocalClientEndpoint = new InMemoryEndpoint("local"),
            BlockTypeKeys = [WorldFactory.StoneBlockKey],
            MaterialKeys = SurvivalContentFactory.PropMaterialKeys,
            Content = SurvivalContentFactory.CreateTestWithProps(),
            ItemTypeKeys = SurvivalContentFactory.PropItemKeys,
            Faults = (IPersistenceFaults?)faults ?? NoFaults.Instance,
        };
        Runtime = new ServerRuntime(ProcessMode.Solo, options, _serverTransport, Terrain, NewWorld, Time, NullLoggerFactory.Instance);
        InventoryService.Register(Runtime, NullLoggerFactory.Instance);
        PropRegrowth.Register(Runtime, () => Terrain.Last.Terrain, NullLoggerFactory.Instance);
        PropReplicator.Register(Runtime, NullLoggerFactory.Instance);
    }

    /// <summary>Radius of the terrain and of the generated surface.</summary>
    public static float Surface => WorldFactory.HomePlanetParams.RadiusMeters + Height;

    public static Vector3 Pole => new(0, Surface, 0);

    public string Directory { get; }

    public FakeTimeProvider Time { get; }

    public FakeServerTerrainFactory Terrain { get; }

    public ServerRuntime Runtime { get; }

    public IReadOnlyList<PropsPlayer> Players => _players;

    public ulong Hash => StateHasher.Hash(Runtime.State);

    public static string WorldPath(string directory) => Path.Combine(directory, "saves", "world.db");

    public static WorldState NewWorld() => WorldFactory.CreateWithProps(0);

    /// <summary>A prop field over the world and the terrain of the rig (its own cache); after <see cref="StartAsync"/>.</summary>
    public PropField NewField() => new(Runtime.State, Terrain.Last.Terrain, Runtime.Simulation.Rules);

    public PropInstance FindProp(PropKindV1 kind) => PropFixtures.FindProp(NewField(), WorldFactory.HomePlanet, Pole, kind);

    public async Task StartAsync() =>
        Assert.IsType<WorldStartResult.Started>(await Runtime.StartAsync(TestContext.Current.CancellationToken));

    public void Step()
    {
        Time.Advance(_frame);
        Runtime.Tick();
        foreach (var player in _players)
        {
            player.Client.Poll();
            player.Containers.Apply(player.Hooks.Messages);
            player.Props.Apply(player.Hooks.Messages);
        }
    }

    public void StepMany(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            Step();
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

    /// <summary>Connects a client, sends ready, puts its body at <paramref name="feet"/> and waits for its containers.</summary>
    public async Task<PropsPlayer> JoinAsync(ulong identity, Vector3? feet = null)
    {
        var transport = _network.CreateTransport($"client{identity}", TransportOptions.Default);
        _clientTransports.Add(transport);
        var hooks = new RecordingClientHooks();
        var client = new ClientSession(transport, new ClientOptions { BuildHash = Build }, hooks, Time);
        var player = new PropsPlayer(client, hooks);
        _players.Add(player);
        client.Connect(new InMemoryEndpoint("server"), new PlatformIdentity(IdentityPlatform.Dev, identity), "player" + identity);
        Assert.True(await RunUntilAsync(() => client.State == ClientState.Loading), "client did not reach Loading");
        client.SendReady();
        Assert.True(await RunUntilAsync(() => client.State == ClientState.InGame), "client did not reach InGame");
        Assert.True(await RunUntilAsync(() => player.Containers.Ids.Count == 3), "the container states did not arrive");
        Place(player.Player, feet ?? Pole);
        return player;
    }

    /// <summary>Moves the body of a player on the server to <paramref name="feet"/> in the planet frame.</summary>
    public void Place(PlayerId player, Vector3 feet)
    {
        Assert.True(Runtime.State.Players.TryGet(player, out var state));
        Assert.True(Runtime.Simulation.UpdatePlayerKinematics(player, state.Frame, state.ParentSeq, feet, Quaternion.Identity, 0f));
    }

    public async Task<IntentReply> SubmitAsync(PropsPlayer player, Intent intent)
    {
        var replies = player.Hooks.Replies.Count;
        player.Client.SubmitIntent(intent);
        Assert.True(await RunUntilAsync(() => player.Hooks.Replies.Count > replies), "no intent reply");
        StepMany(3);
        return player.Hooks.Replies[^1];
    }

    /// <summary>Waits a quarter of a second of ticks (a token of the hit bucket), then submits one hit with the tool in hotbar slot 0.</summary>
    public async Task<IntentReply> HitAsync(PropsPlayer player, PropId prop)
    {
        StepMany(20);
        return await SubmitAsync(player, new PropHitIntent(WorldFactory.HomePlanet, prop, 0));
    }

    /// <summary>Hits a prop until the server harvests it; the last reply is returned. At most <paramref name="limit"/> hits.</summary>
    public async Task<IntentReply> HarvestAsync(PropsPlayer player, PropInstance prop, int limit = 10)
    {
        IntentReply reply = default!;
        for (var i = 0; i < limit; i++)
        {
            reply = await HitAsync(player, prop.Id);
            Assert.Equal(RejectReason.None, reply.Reason);
            if (reply.AppliedOpId != OpId.None)
            {
                return reply;
            }
        }

        throw new InvalidOperationException($"The prop {prop.Id.Value} was not harvested in {limit} hits.");
    }

    /// <summary>Puts the player beside a prop on the surface, <paramref name="horizontalMeters"/> from its anchor.</summary>
    public void PlaceBeside(PropsPlayer player, PropInstance prop, float horizontalMeters) =>
        Place(player.Player, PropFixtures.PointBeside(prop, horizontalMeters, Surface + 0.05f));

    /// <summary>All items of a type in the containers of a player, on the server.</summary>
    public int Count(PlayerId player, string itemKey)
    {
        var type = Runtime.State.ItemTypes.TryGetId(itemKey, out var id) ? id : throw new ArgumentException($"No item '{itemKey}'.", nameof(itemKey));
        var total = 0;
        foreach (var container in Runtime.State.Containers.All)
        {
            if (container.Owner != player)
            {
                continue;
            }

            foreach (var stack in container.Slots.ToArray())
            {
                if (!stack.IsEmpty && stack.Type == type)
                {
                    total += stack.Count;
                }
            }
        }

        return total;
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
