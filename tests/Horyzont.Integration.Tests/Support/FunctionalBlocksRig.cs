using System.Diagnostics;
using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Motion;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Server.Characters;
using Horyzont.Game.Server.Replication;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Persistence;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Terrain;
using Horyzont.Server;
using Horyzont.Server.Inventory;
using Horyzont.Server.Replication;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Integration.Tests.Support;

/// <summary>Characters that the test places by hand (the server body of a rig player).</summary>
internal sealed class RigCharacters : IServerCharacterSource
{
    public Dictionary<PlayerId, ServerCharacterState> States { get; } = [];

    public bool TryGetState(PlayerId player, out ServerCharacterState state) => States.TryGetValue(player, out state);
}

/// <summary>A client of the rig with everything the server told it.</summary>
internal sealed class RigPlayer(ClientSession client, RecordingClientHooks hooks, ContainerMirror mirror)
{
    public ClientSession Client { get; } = client;

    public RecordingClientHooks Hooks { get; } = hooks;

    public ContainerMirror Mirror { get; } = mirror;

    public PlayerId Player => Hooks.Hello!.YourPlayer;

    /// <summary>The latest states of the blocks of a construct this client received (message 46).</summary>
    public BlockEntityStates? StatesOf(EntityId construct) =>
        Hooks.Messages.OfType<BlockEntityStates>().LastOrDefault(m => m.Construct == construct);

    /// <summary>The operations of other clients' effects this client received.</summary>
    public IEnumerable<OperationApplied> Applied => Hooks.Messages.OfType<OperationApplied>();
}

/// <summary>
/// A solo server with the survival rules, the functional blocks of K6-30, the chest subscriptions and the entity replication over an
/// in-memory network and any number of clients. Several rigs may share a world directory one after another to restart the server.
/// </summary>
internal sealed class FunctionalBlocksRig : IAsyncDisposable
{
    public const ulong Build = 0x4B36_3000_0000_0001UL;

    /// <summary>Slots of the chest container of the rig's block content.</summary>
    public const ushort ChestSlots = 8;

    private static readonly TimeSpan _frame = TimeSpan.FromMilliseconds(16);

    private readonly InMemoryNetwork _network;
    private readonly InMemoryTransport _serverTransport;
    private readonly List<InMemoryTransport> _clientTransports = [];
    private readonly RigCharacters _characters = new();
    private readonly List<RigPlayer> _players = [];

    public FunctionalBlocksRig(string directory, ArmedFaults? faults = null)
    {
        Directory = directory;
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        Terrain = new FakeServerTerrainFactory(Time);
        _network = new InMemoryNetwork(Time);
        _serverTransport = _network.CreateTransport("server", TransportOptions.Default);
        var options = new ServerRuntimeOptions
        {
            WorldPath = WorldPath(directory),
            ServerOptions = new ServerOptions { BuildHash = Build },
            PersistenceOptions = new PersistenceOptions { GameVersion = "k6-30-test", BackupInterval = Timeout.InfiniteTimeSpan },
            Listen = [new InMemoryEndpoint("server")],
            LocalClientEndpoint = new InMemoryEndpoint("local"),
            BlockTypeKeys = FunctionalBlocks.BlockKeys,
            BlockDefinitions = FunctionalBlocks.DefinitionsFor(ChestSlots),
            MaterialKeys = ["stone", "dirt"],
            Content = SurvivalContentFactory.CreateTest(),
            ItemTypeKeys = SurvivalContentFactory.ItemKeys,
            Faults = (IPersistenceFaults?)faults ?? NoFaults.Instance,
        };
        Runtime = new ServerRuntime(ProcessMode.Solo, options, _serverTransport, Terrain, NewWorld, Time, NullLoggerFactory.Instance);
        InventoryService.Register(Runtime, NullLoggerFactory.Instance);
        new EntityReplicationBinder(runtime => new ServerReplicationSource(runtime.State, _characters), Time, NullLoggerFactory.Instance).Register(Runtime);
    }

    public string Directory { get; }

    public FakeTimeProvider Time { get; }

    public FakeServerTerrainFactory Terrain { get; }

    public ServerRuntime Runtime { get; }

    public IReadOnlyList<RigPlayer> Players => _players;

    public ulong Hash => StateHasher.Hash(Runtime.State);

    /// <summary>The one construct of the rig's world (made by <see cref="BuildConstruct"/>).</summary>
    public Construct Construct => Runtime.State.Constructs.All[0];

    public static string WorldPath(string directory) => Path.Combine(directory, "saves", "world.db");

    public static WorldState NewWorld() => WorldState.CreateNew(
        WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], FunctionalBlocks.BlockKeys, ["stone", "dirt"], SurvivalContentFactory.ItemKeys);

    public async Task StartAsync() =>
        Assert.IsType<WorldStartResult.Started>(await Runtime.StartAsync(TestContext.Current.CancellationToken));

    public void Step()
    {
        Time.Advance(_frame);
        Runtime.Tick();
        foreach (var player in _players)
        {
            player.Client.Poll();
            player.Mirror.Apply(player.Hooks.Messages);
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

    /// <summary>
    /// A stone floor of five blocks in a construct on the north pole of the home planet (the first run of a world only). The plain blocks of
    /// <paramref name="extra"/> are set next to it; the lamp starts off, the door closed.
    /// </summary>
    public EntityId BuildConstruct(params (CellPos Cell, ushort Type)[] extra)
    {
        var sim = Runtime.Simulation;
        var id = Runtime.State.EntityIds.PeekNext();
        sim.ApplyServerOperation(new ConstructCreateOp(
            id, FrameRef.Of(WorldFactory.HomePlanet), new Vector3(0, 4000, 0), Quaternion.Identity, FunctionalBlocks.FloorCells[0],
            BlockState.Create(FunctionalBlocks.StoneType, 0)));
        foreach (var cell in FunctionalBlocks.FloorCells.Skip(1))
        {
            sim.ApplyServerOperation(new BlockSetOp(id, cell, BlockState.Create(FunctionalBlocks.StoneType, 0)));
        }

        foreach (var (cell, type) in extra)
        {
            sim.ApplyServerOperation(new BlockSetOp(id, cell, BlockState.Create(type, 0)));
        }

        return id;
    }

    /// <summary>Connects a client, sends ready, puts its body 3 m in front of the blocks and waits for its containers and the construct.</summary>
    public async Task<RigPlayer> JoinAsync(ulong identity, bool expectConstruct = true)
    {
        var transport = _network.CreateTransport($"client{identity}", TransportOptions.Default);
        _clientTransports.Add(transport);
        var hooks = new RecordingClientHooks();
        var client = new ClientSession(transport, new ClientOptions { BuildHash = Build }, hooks, Time);
        var mirror = new ContainerMirror();
        var player = new RigPlayer(client, hooks, mirror);
        _players.Add(player);
        client.Connect(new InMemoryEndpoint("server"), new PlatformIdentity(IdentityPlatform.Dev, identity), "player" + identity);
        Assert.True(await RunUntilAsync(() => client.State == ClientState.Loading), "client did not reach Loading");
        client.SendReady();
        Assert.True(await RunUntilAsync(() => client.State == ClientState.InGame), "client did not reach InGame");
        Assert.True(await RunUntilAsync(() => mirror.Ids.Count == 3), "the container states did not arrive");
        Place(player.Player, new Vector3(1.5f + (0.25f * (_players.Count - 1)), 4001f, 3f));
        if (expectConstruct && Runtime.State.Constructs.Count > 0)
        {
            var construct = Construct.Id;
            Assert.True(await RunUntilAsync(() => hooks.Messages.OfType<ConstructState>().Any(m => m.Header.Id == construct)), "the construct did not arrive");
        }

        return player;
    }

    /// <summary>Moves the body of a player (server simulation and the replication source) to <paramref name="feet"/> in the planet frame.</summary>
    public void Place(PlayerId player, Vector3 feet)
    {
        Assert.True(Runtime.State.Players.TryGet(player, out var state));
        Assert.True(Runtime.Simulation.UpdatePlayerKinematics(player, state.Frame, state.ParentSeq, feet, Quaternion.Identity, 0f));
        _characters.States[player] = new ServerCharacterState(
            WorldFactory.HomePlanet, FrameRef.Of(WorldFactory.HomePlanet), 0, feet, Quaternion.Identity, 0, Vector3.Zero, MovementMode.Walking, 0);
    }

    public async Task<IntentReply> SubmitAsync(RigPlayer player, Intent intent)
    {
        var replies = player.Hooks.Replies.Count;
        player.Client.SubmitIntent(intent);
        Assert.True(await RunUntilAsync(() => player.Hooks.Replies.Count > replies), "no intent reply");
        StepMany(3);
        return player.Hooks.Replies[^1];
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
