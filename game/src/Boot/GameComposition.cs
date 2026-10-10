using System.Globalization;

using Godot;

using Horyzont.Client.Replication;
using Horyzont.Client.Session;
using Horyzont.Core;
using Horyzont.Core.Hashing;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Game.Client;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Client.Sky;
using Horyzont.Game.Client.Tools;
using Horyzont.Game.Constructs;
using Horyzont.Game.Planet;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Planet.Rendering;
using Horyzont.Game.Server;
using Horyzont.Game.Server.Characters;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Net.Transport.Udp;
using Horyzont.Persistence.Backup;
using Horyzont.Server;
using Horyzont.Server.Console;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Boot;

/// <summary>
/// The composition root: the only place that creates the services of a game process (ZASADY, point 2). It builds the scene
/// <c>Root.tscn</c> (<c>/Root/Server</c> and <c>/Root/Client</c>, whichever the mode needs), the server runtime, the client runtime,
/// the transports and the terrain providers of the chosen <see cref="ProcessMode"/>, and wires them together. Nothing here is static
/// state, so tests compose several instances in one process. Further services (K2-13 constructs, K2-11 characters) are added to the
/// nodes and the runtimes after <see cref="Create"/> and before <see cref="StartAsync"/>, without changing the start and shutdown order.
/// </summary>
public sealed partial class GameComposition
{
    private PassThroughFullSnapshots? _fullSnapshots;
    private const string RootScene = "res://scenes/Root.tscn";
    private const string ToolsScene = "res://scenes/client/tools/Tools.tscn";

    /// <summary>How long the quit coordinator waits for the final cleanup (the client transport) before it quits anyway.</summary>
    private static readonly TimeSpan _releaseTimeout = TimeSpan.FromSeconds(5);

    private readonly LaunchOptions _launch;
    private readonly GameEnvironment _env;
    private readonly ILogger<GameComposition> _logger;
    private readonly ITransport? _serverTransport;
    private readonly ITransport? _clientTransport;
    private readonly string? _savesDir;
    private readonly ServerConfig? _config;
    private readonly LocalProfile? _profile;
    private readonly Guid _worldId;
    private readonly bool _worldIdFromConfig;
    private readonly OwnTerrainProvider? _ownTerrain;
    private readonly ServerCharactersBinder? _characters;
    private bool _clientEnded;
    private bool _serverRan;
    private bool _released;
    private bool _clientStopRequested;
    private int _clientExitCode;
    private int _failureExitCode;
    private Task? _transportDispose;
    private bool _transportDisposeLogged;

    private GameComposition(
        LaunchOptions launch, GameEnvironment env, Node root, ITransport? serverTransport, ITransport? clientTransport, ServerRuntime? server,
        ClientRuntime? client, ServerNode? serverNode, ClientNode? clientNode, VoxelServerTerrainFactory? terrainFactory, OwnTerrainProvider? ownTerrain,
        string? savesDir, ServerConfig? config, LocalProfile? profile, Guid worldId, bool worldIdFromConfig, ServerCharactersBinder? characters,
        LocalPlayer? localPlayer, ClientDayNight? dayNight, HostConstructs? constructs, ToolNode? tools)
    {
        Constructs = constructs;
        Tools = tools;
        _characters = characters;
        LocalPlayer = localPlayer;
        DayNight = dayNight;
        _launch = launch;
        _env = env;
        _logger = env.Loggers.CreateLogger<GameComposition>();
        Root = root;
        _serverTransport = serverTransport;
        _clientTransport = clientTransport;
        Server = server;
        Client = client;
        ServerNode = serverNode;
        ClientNode = clientNode;
        TerrainFactory = terrainFactory;
        _ownTerrain = ownTerrain;
        _savesDir = savesDir;
        _config = config;
        _profile = profile;
        _worldId = worldId;
        _worldIdFromConfig = worldIdFromConfig;
    }

    /// <summary>Gets the mode of the process.</summary>
    public ProcessMode Mode => _launch.Mode;

    /// <summary>Gets the <c>Root</c> node; add it to the scene tree before <see cref="StartAsync"/>.</summary>
    public Node Root { get; }

    /// <summary>Gets the server runtime; null in client mode.</summary>
    public ServerRuntime? Server { get; }

    /// <summary>Gets the server node (<c>/Root/Server</c>); null in client mode.</summary>
    public ServerNode? ServerNode { get; }

    /// <summary>Gets the client runtime; null in dedicated server mode.</summary>
    public ClientRuntime? Client { get; }

    /// <summary>Gets the client node (<c>/Root/Client</c>); null in dedicated server mode.</summary>
    public ClientNode? ClientNode { get; }

    /// <summary>Gets the factory that makes the server terrain; null in client mode.</summary>
    public VoxelServerTerrainFactory? TerrainFactory { get; }

    /// <summary>Gets the terrain the client sees; set by <see cref="StartAsync"/> in solo and host mode.</summary>
    public IClientTerrainProvider? ClientTerrain => ClientNode?.TerrainProvider;

    /// <summary>Gets the characters of the server (null in client mode, with characters off, and before the world has started).</summary>
    public ServerCharacters? ServerCharacters => _characters?.Characters;

    /// <summary>Gets the sender of the own-state snapshots (solo, host and dedicated server); null before the world has started.</summary>
    public OwnStateSender? OwnState => _characters?.Sender;

    /// <summary>Gets the local player (prediction, camera); null in dedicated server mode and with characters off.</summary>
    public LocalPlayer? LocalPlayer { get; }

    /// <summary>Gets the sun and sky of the client; null in dedicated server mode and with characters off.</summary>
    public ClientDayNight? DayNight { get; }

    /// <summary>Gets the constructs of the server (K2-12 wired in by K2-13); null in client mode.</summary>
    public HostConstructs? Constructs { get; }

    /// <summary>Gets the tools of the local player (K2-13); null in dedicated server mode, in client mode and with characters off.</summary>
    public ToolNode? Tools { get; }

    /// <summary>Gets the movement rules the server characters and the local player share in this process.</summary>
    public MovementRules Movement { get; private init; } = new();

    /// <summary>Gets the world file; null in client mode.</summary>
    public string? WorldPath => _savesDir is null ? null : WorldPathOf(_savesDir, _worldId);

    /// <summary>Gets the world id of the server; <see cref="Guid.Empty"/> in client mode.</summary>
    public Guid WorldId => _worldId;

    /// <summary>Gets a value indicating whether the process may exit: the server (or the remote client) completed its shutdown.</summary>
    public bool IsFinished => Server is not null ? Server.IsStopped : _clientEnded && (_clientTransport?.IsDrained ?? true);

    /// <summary>Gets the result of the latest world start of the server; null in client mode and before the start has completed.</summary>
    public WorldStartResult? LastWorldStartResult { get; private set; }

    /// <summary>Raised on the main thread when <c>Server.StartAsync</c> has returned, before the result is acted on (the menu shows it).</summary>
    public event Action<WorldStartResult>? WorldStartCompleted;

    /// <summary>Gets the exit code of the process; valid once <see cref="IsFinished"/>.</summary>
    public int ExitCode => Server is null ? _clientExitCode : Server.ExitCode != 0 ? Server.ExitCode : _failureExitCode;

    /// <summary>Builds the services and the scene of the mode. Main thread; nothing is started and nothing enters the scene tree yet.</summary>
    /// <param name="launch">The parsed command line.</param>
    /// <param name="env">The process around the game.</param>
    /// <returns>The composition.</returns>
    /// <exception cref="ArgumentException">The server configuration file is missing or invalid, or <c>--dev-tools-demo</c> is used in a build that is not a development build.</exception>
    public static GameComposition Create(LaunchOptions launch, GameEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(env);
        if (launch.DevToolsDemo && !env.DevBuild)
        {
            throw new ArgumentException("--dev-tools-demo works only in a development build (debug build of the game).");
        }

        var scene = GD.Load<PackedScene>(RootScene) ?? throw new InvalidOperationException($"Scene {RootScene} not found.");
        var root = scene.Instantiate<Node>();
        var serverNode = root.GetNode<ServerNode>("Server");
        var clientNode = root.GetNode<ClientNode>("Client");
        var hasServer = launch.Mode != ProcessMode.Client;
        var hasClient = launch.Mode != ProcessMode.DedicatedServer;
        if (!hasServer)
        {
            root.RemoveChild(serverNode);
            serverNode.Free();
            serverNode = null;
        }

        if (!hasClient)
        {
            root.RemoveChild(clientNode);
            clientNode.Free();
            clientNode = null;
        }

        var buildHash = BuildInfo.ComputeBuildHash(GeneratorProfiles.BuildHashComponent);
        var loggers = env.Loggers;
        var rules = new GameRules();
        var movement = ScaledMovement(launch.DevSpeed);
        // Solo and host own the server, so the build decides. A dedicated server follows its --dev flag, and a connecting client must be
        // started with --dev too (ServerHello does not carry the permission); the server has to run with --dev as well.
        var devFlight = launch.Mode is ProcessMode.DedicatedServer or ProcessMode.Client ? launch.Dev : env.DevBuild;
        ServerCharactersBinder? characters = null;
        LocalPlayer? localPlayer = null;
        ClientDayNight? dayNight = null;
        HostConstructs? constructs = null;
        ToolNode? tools = null;
        ServerRuntime? server = null;
        ClientRuntime? client = null;
        ITransport? serverTransport = null;
        ITransport? clientTransport = null;
        VoxelServerTerrainFactory? factory = null;
        OwnTerrainProvider? own = null;
        ServerConfig? config = null;
        LocalProfile? profile = null;
        string? savesDir = null;
        var worldId = Guid.Empty;
        var fromConfig = false;

        if (hasServer)
        {
            var dedicated = launch.Mode == ProcessMode.DedicatedServer;
            if (dedicated)
            {
                if (!ServerConfigLoader.TryLoad(launch.ConfigPath!, out var loaded, out var error))
                {
                    throw new ArgumentException($"Server configuration: {error}");
                }

                config = loaded;
            }

            var dataDir = launch.DataDir ?? (dedicated ? env.ExecutableDir : env.UserDataDir);
            savesDir = Path.Combine(dataDir, "saves");
            fromConfig = config?.WorldId is not null;
            worldId = (dedicated ? config!.WorldId : launch.WorldId) ?? Guid.NewGuid();
            var worldPath = WorldPathOf(savesDir, worldId);

            var terrainParent = new Node3D { Name = "Terrain" };
            serverNode!.AddChild(terrainParent);
            var options = dedicated ? PlanetTerrainOptions.Server : new PlanetTerrainOptions(Collisions: true, Render: env.Render);
            factory = new VoxelServerTerrainFactory(terrainParent, options, env.Time, loggers);

            var listen = new List<TransportEndpoint>();
            InMemoryNetwork? network = null;
            ITransport udp;
            switch (launch.Mode)
            {
                case ProcessMode.Solo:
                case ProcessMode.Host:
                    network = new InMemoryNetwork(env.Time, env.LocalLink);
                    var local = network.CreateTransport("server", TransportOptions.Default);
                    listen.Add(new InMemoryEndpoint("server"));
                    if (launch.Mode == ProcessMode.Host)
                    {
                        udp = new UdpTransport(TransportOptions.Default, loggers.CreateLogger<UdpTransport>(), env.Time);
                        listen.Add(new UdpEndpoint(launch.BindAddress, launch.Port));
                        serverTransport = new CompositeTransport([local, udp]);
                    }
                    else
                    {
                        serverTransport = local;
                    }

                    clientTransport = network.CreateTransport("local", TransportOptions.Default);
                    break;
                default:
                    listen.Add(config!.ToListenEndpoint());
                    serverTransport = new UdpTransport(TransportOptions.Default, loggers.CreateLogger<UdpTransport>(), env.Time);
                    break;
            }

            var catalog = BlockContentLoader.Load();
            var runtimeOptions = new ServerRuntimeOptions
            {
                WorldPath = worldPath,
                BackupsDirectory = Path.Combine(Path.GetDirectoryName(worldPath)!, "backups"),
                ServerOptions = dedicated
                    ? config!.ToServerOptions(buildHash)
                    : new ServerOptions { BuildHash = buildHash, Password = launch.Password, MaxPlayers = launch.Mode == ProcessMode.Solo ? 1 : 8 },
                PersistenceOptions = dedicated
                    ? config!.ToPersistenceOptions(BuildInfo.Commit)
                    : new Horyzont.Persistence.PersistenceOptions { GameVersion = BuildInfo.Commit },
                Listen = listen,
                LocalClientEndpoint = dedicated ? null : new InMemoryEndpoint("local"),
                CheckpointInterval = dedicated ? config!.CheckpointInterval : TimeSpan.FromSeconds(60),
                BlockTypeKeys = catalog.BlockTypeKeys,
                MaterialKeys = TerrainMaterials.Keys,
                RecoverFromCheckpoint = launch.RecoverFromCheckpoint,
                Rules = rules,
                NewPlayerSpawn = env.Characters ? SpawnLocator.ForNewPlayer : null,
            };
            var seed = launch.GalaxySeed;
            server = new ServerRuntime(
                launch.Mode, runtimeOptions, serverTransport, factory, () => NewWorld(worldId, seed, catalog.BlockTypeKeys), env.Time, loggers);
            constructs = new HostConstructs(serverNode!, catalog, env.Render, loggers, env.Time);
            constructs.Register(server);

            if (env.Characters)
            {
                characters = new ServerCharactersBinder(serverNode!, factory, rules, movement, devFlight, loggers, constructs.AreSpawnCollisionsReady);
                characters.Register(server);
            }

            DedicatedConsole? console = null;
            if (dedicated && env.ConsoleInput is not null)
            {
                console = new DedicatedConsole(
                    new ConsoleInputPump(env.ConsoleInput, loggers.CreateLogger<ConsoleInputPump>()),
                    new ConsoleCommandProcessor(server, loggers.CreateLogger<ConsoleCommandProcessor>(), launch.Dev),
                    env.Print);
            }

            serverNode.Initialize(server, console, env.Time);
        }

        if (hasClient)
        {
            profile = LocalProfile.LoadOrCreate(launch.ProfilePath ?? Path.Combine(env.UserDataDir, "profile.json"));
            var isHostLocal = hasServer;
            if (!isHostLocal)
            {
                clientTransport = new UdpTransport(TransportOptions.Default, loggers.CreateLogger<UdpTransport>(), env.Time);
            }

            client = new ClientRuntime(clientTransport!, new ClientOptions { BuildHash = buildHash }, isHostLocal, env.Time, loggers);
            clientNode!.Initialize(client, loggers.CreateLogger<ClientNode>());
            if (env.Characters)
            {
                (localPlayer, dayNight) = CreateClientCharacter(clientNode, client, server, env, rules, movement, devFlight);
                if (isHostLocal)
                {
                    tools = CreateTools(clientNode, client, server!, factory!, localPlayer, rules, env, launch.DevToolsDemo);
                }
            }

            if (!isHostLocal)
            {
                var terrainParent = new Node3D { Name = "Terrain" };
                clientNode.AddChild(terrainParent);
                var material = env.Render ? PlanetTerrainMaterial.Create() : null;
                own = new OwnTerrainProvider(
                    terrainParent, new PlanetTerrainOptions(Collisions: true, Render: env.Render), material, loggers.CreateLogger<OwnTerrainProvider>());
                clientNode.TerrainProvider = own;
            }
        }

        var composition = new GameComposition(
            launch, env, root, serverTransport, clientTransport, server, client, serverNode, clientNode, factory, own, savesDir, config, profile,
            worldId, fromConfig, characters, localPlayer, dayNight, constructs, tools)
        {
            Movement = movement,
        };
        if (client is not null && localPlayer is not null)
        {
            composition._fullSnapshots = new PassThroughFullSnapshots();
            composition._fullSnapshots.AddSink(localPlayer);
            client.AddListener(composition._fullSnapshots);
        }

        composition.ConfigureReplication();
        composition.ConfigureTerrainReplication();
        composition.ConfigureMotion();
        composition.ConfigurePlatform();

        if (env.Characters && env.Render && launch.ScreenshotPath is { } screenshot && clientNode is not null)
        {
            root.AddChild(new ScreenshotHook(screenshot, launch.ScreenshotAfterSeconds, env.Print));
        }

        if (clientNode is not null)
        {
            clientNode.SessionEnded += composition.OnClientEnded;
            clientNode.ConnectionRejected += composition.OnClientRejected;
        }

        if (env.ManageQuit)
        {
            var quit = new QuitCoordinator { Name = "Quit" };
            quit.Initialize(
                composition.RequestShutdown, () => composition.IsFinished, () => composition.ExitCode, composition.ReleaseStep, env.RegisterSignals,
                loggers.CreateLogger<QuitCoordinator>(), env.Time, _releaseTimeout);
            root.AddChild(quit);
            composition.Quit = quit;
        }

        return composition;
    }

    partial void ConfigureReplication();
    partial void ConfigureTerrainReplication();
    partial void ConfigureMotion();
    partial void ConfigurePlatform();

    /// <summary>Walking and sprinting speed scaled by <c>--dev-speed</c> (the accelerations scale with it so the character reaches the speed as fast as before).</summary>
    private static MovementRules ScaledMovement(float factor)
    {
        var movement = new MovementRules();
        return factor == 1f
            ? movement
            : movement with
            {
                WalkSpeed = movement.WalkSpeed * factor,
                SprintSpeed = movement.SprintSpeed * factor,
                GroundAcceleration = movement.GroundAcceleration * factor,
                GroundDeceleration = movement.GroundDeceleration * factor,
            };
    }

    private static (LocalPlayer Player, ClientDayNight Sky) CreateClientCharacter(
        ClientNode clientNode, ClientRuntime client, ServerRuntime? server, GameEnvironment env, GameRules rules, MovementRules movement,
        bool devFlight)
    {
        var reader = new LocalInputReader { Name = "Input" };
        var rig = new CameraRig();
        DevFlyCamera? dev = devFlight ? new DevFlyCamera(movement) : null;
        var player = new LocalPlayer { Name = "LocalPlayer" };
        player.Initialize(clientNode, client, rig, dev, reader, rules, movement, devFlight, env.Render, env.Loggers);
        Func<ulong?>? hostTick = server is null
            ? null
            : () => server.RuntimeState is ServerRuntimeState.Running or ServerRuntimeState.SafeMode ? server.State.CurrentTick.Value : null;
        var sky = new ClientDayNight(player, hostTick, env.Render);
        sky.Initialize(client);
        clientNode.AddChild(reader);
        clientNode.AddChild(rig);
        if (dev is not null)
        {
            clientNode.AddChild(dev);
        }

        clientNode.AddChild(player);
        clientNode.AddChild(sky);
        if (env.Render)
        {
            clientNode.AddChild(new HelpOverlay(player, sky, dev));
        }

        return (player, sky);
    }

    /// <summary>
    /// The tools of the local player of a solo or host process (K2-13): they read the authoritative terrain and state of the server in
    /// this process, so the edit prediction is <c>NoEditPrediction</c> (decision 7).
    /// </summary>
    private static ToolNode CreateTools(
        ClientNode clientNode, ClientRuntime client, ServerRuntime server, VoxelServerTerrainFactory factory, LocalPlayer player, GameRules rules,
        GameEnvironment env, bool demo)
    {
        var scene = GD.Load<PackedScene>(ToolsScene) ?? throw new InvalidOperationException($"Scene {ToolsScene} not found.");
        var tools = scene.Instantiate<ToolNode>();
        tools.Initialize(client, server, () => factory.Terrain!.Service, player, rules, env.Loggers);
        if (env.Render)
        {
            tools.AddChild(new TerrainToolPreview(tools));
            tools.AddChild(new BlockGhostPreview(tools));
            tools.AddChild(new ToolHud(tools));
        }

        if (demo)
        {
            var script = new ToolsDemo(tools, player.InputSource);
            player.InputSource = script;
            tools.InputSource = script;
            tools.AddChild(script);
        }

        clientNode.AddChild(tools);
        return tools;
    }

    private QuitCoordinator? Quit { get; set; }

    /// <summary>
    /// Starts the server (a dedicated server first applies <c>--restore-latest-backup</c>), then connects the client: the local one
    /// of a solo or host process after the server listens, the remote one at once. Main thread; the <see cref="Root"/> must be in
    /// the scene tree.
    /// </summary>
    /// <param name="ct">Cancellation of the start.</param>
    /// <param name="connectClient">Whether the local client of a solo or host process connects (tests start a server alone).</param>
    /// <returns>Null while the process runs on; otherwise the exit code the process must end with (the world did not start).</returns>
    public async Task<int?> StartAsync(CancellationToken ct, bool connectClient = true)
    {
        try
        {
            if (Server is not null)
            {
                var outcome = await StartServerAsync(ct).ConfigureAwait(true);
                if (outcome is { } exit)
                {
                    Release();
                    return exit;
                }
            }

            ConnectClient(connectClient);
        }
#pragma warning disable CA1031 // The server already runs: any failure of the rest of the start must end it through the canonical shutdown, not abandon it.
        catch (Exception ex) when (Server is { RuntimeState: ServerRuntimeState.Running or ServerRuntimeState.SafeMode })
#pragma warning restore CA1031
        {
            FailAfterServerStart(ex);
        }

        return null;
    }

    /// <summary>
    /// Connects the local client of a solo or host process to the server that already runs. <c>StartAsync(ct, connectClient: false)</c>
    /// leaves this to the caller, which first looks at <see cref="LastWorldStartResult"/> (the menu does not join a read-only world).
    /// </summary>
    /// <exception cref="InvalidOperationException">This is not a solo or host process, or its server does not run.</exception>
    public void ConnectLocalClient()
    {
        if (Server is null || Client is null || ClientNode?.TerrainProvider is null)
        {
            throw new InvalidOperationException("ConnectLocalClient needs a started solo or host composition.");
        }

        Client.Connect(new InMemoryEndpoint("server"), _profile!.Identity, _profile.DisplayName, _launch.Password);
    }

    private void ConnectClient(bool connectClient)
    {
        if (Client is null)
        {
            return;
        }

        if (Server is not null)
        {
            ClientNode!.TerrainProvider = new SharedTerrainProvider(TerrainFactory!.Terrain!.Voxel);
            if (connectClient)
            {
                ConnectLocalClient();
            }
        }
        else
        {
            Client.Connect(new UdpEndpoint(_launch.ConnectHost!, _launch.ConnectPort), _profile!.Identity, _profile.DisplayName, _launch.Password);
        }
    }

    /// <summary>
    /// A failure after the server reached Running: the process still ends with the canonical shutdown (checkpoint, flush, writer,
    /// terrain, with their deadlines) and then with the error code, instead of dropping the live world.
    /// </summary>
    private void FailAfterServerStart(Exception exception)
    {
        _logger.LogStartFailedAfterRunning(exception);
        _failureExitCode = Boot.ExitFaulted;
        if (Quit is not null)
        {
            Quit.RequestStop();
        }
        else
        {
            RequestShutdown();
        }
    }

    /// <summary>Starts the canonical shutdown: the server's, or the disconnect of a remote client. A second call is ignored by the server.</summary>
    public void RequestShutdown()
    {
        if (Server is not null)
        {
            Server.RequestShutdown();
        }
        else if (!_clientStopRequested)
        {
            _clientStopRequested = true;
            Client?.Session.Disconnect();
            _clientEnded = true;
        }
    }

    /// <summary>
    /// Last cleanup after the server stopped (or did not start): the local client of a solo or host process leaves after the server
    /// (it gets <c>HostLeft</c> through the in-memory transport while the server drains) and its transport is disposed, here on the
    /// calling thread. Idempotent; the quit coordinator uses <see cref="ReleaseStep"/> instead.
    /// </summary>
    public void Release() => BeginRelease(inline: true);

    /// <summary>
    /// The same cleanup as <see cref="Release"/> for the quit coordinator: the transport of the client is disposed on a worker thread
    /// (LiteNetLib stops its thread with an unbounded join), so the main thread keeps running frames while it waits.
    /// </summary>
    /// <returns>True when the cleanup is complete.</returns>
    public bool ReleaseStep()
    {
        BeginRelease(inline: false);
        if (_transportDispose is not { IsCompleted: true } done)
        {
            return _transportDispose is null;
        }

        if (done.IsFaulted && !_transportDisposeLogged)
        {
            _transportDisposeLogged = true;
            _logger.LogTransportDisposeFailed(done.Exception!.GetBaseException());
        }

        return true;
    }

    private void BeginRelease(bool inline)
    {
        if (_released)
        {
            return;
        }

        _released = true;
        if (Client is not null)
        {
            Client.Session.Disconnect();
            Client.Poll();
        }

        _ownTerrain?.Dispose();
        Constructs?.Dispose();
        if (Server is not null && !_serverRan)
        {
            // The world did not start: the shutdown sequence never ran, so the caller owns the server transport.
            _serverTransport?.Dispose();
        }

        if (_clientTransport is { } transport)
        {
            if (inline)
            {
                transport.Dispose();
            }
            else
            {
                _transportDispose = Task.Run(transport.Dispose);
            }
        }
    }

    private static string WorldPathOf(string savesDir, Guid id) => Path.Combine(savesDir, id.ToString("D"), "world.db");

    private static WorldState NewWorld(Guid id, ulong seed, IReadOnlyList<string> blockTypeKeys) =>
        WorldState.CreateNew(
            new Horyzont.Core.Ids.WorldId(id),
            seed,
            [PlanetGenesis.CreatePlanet(seed, PlanetGenesis.HomePlanetIndex, GeneratorProfiles.Current)],
            blockTypeKeys,
            TerrainMaterials.Keys);

    private async Task<int?> StartServerAsync(CancellationToken ct)
    {
        var path = WorldPath!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (_launch.RestoreLatestBackup && File.Exists(path))
        {
            var backups = Path.Combine(Path.GetDirectoryName(path)!, "backups");
            var restored = WorldBackups.RestoreLatest(path, backups, _env.Time);
            if (restored is null)
            {
                _env.Print($"SERVER START FAILED: no backup in '{backups}' to restore.");
                return 3;
            }

            _env.Print($"RESTORED world from '{restored.RestoredFrom}'; the old file is '{restored.CorruptPath}'");
        }

        var result = await Server!.StartAsync(ct).ConfigureAwait(true);
        LastWorldStartResult = result;
        WorldStartCompleted?.Invoke(result);
        var world = _worldId.ToString("D");
        switch (result)
        {
            case WorldStartResult.Started:
                _serverRan = true;
                Constructs?.Attach(Server!);
                RememberLastWorld();
                _env.Print($"SERVER READY world={world} state hash={StateHasher.Hash(Server.State).ToString("X16", CultureInfo.InvariantCulture)}");
                return null;
            case WorldStartResult.StartedReadOnly readOnly:
                _serverRan = true;
                Constructs?.Attach(Server!);
                RememberLastWorld();
                _env.Print($"SERVER READ-ONLY world={world} reason={readOnly.Reason}: {readOnly.Message}");
                return null;
            case WorldStartResult.Corrupted corrupted:
                _env.Print($"WORLD UNUSABLE world={world}: {corrupted.Message}");
                _env.Print(corrupted.Backups.Count == 0
                    ? "No backups exist."
                    : string.Create(CultureInfo.InvariantCulture, $"Backups ({corrupted.Backups.Count}), newest last:\n  {string.Join("\n  ", corrupted.Backups)}"));
                _env.Print("Restart with --restore-latest-backup to replace the world file with the newest backup (the damaged file is kept).");
                return result.ExitCode;
            case WorldStartResult.FromNewerVersion newer:
                _env.Print($"WORLD FROM A NEWER VERSION world={world}: schema {newer.SchemaVersion}, this game supports {newer.SupportedVersion}.");
                return result.ExitCode;
            case WorldStartResult.ReplayFailed failed:
                _env.Print($"REPLAY FAILED world={world} op={failed.OpId.Value}: {failed.Message}");
                return result.ExitCode;
            case WorldStartResult.Aborted:
                return result.ExitCode;
            default:
                _env.Print($"SERVER START FAILED world={world}: {(result as WorldStartResult.StartFailed)?.Message}");
                return result.ExitCode;
        }
    }

    private void RememberLastWorld()
    {
        if (_launch.Mode != ProcessMode.DedicatedServer || _worldIdFromConfig)
        {
            return;
        }

        // The configuration is never rewritten; the id of a world this run created goes to a side file for the operator.
        // A hint for the operator, not part of the world: a failure here must not stop a running server.
        try
        {
            File.WriteAllText(Path.Combine(_savesDir!, "last-world.txt"), _worldId.ToString("D") + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogLastWorldNotWritten(ex);
        }
    }

    private void OnClientEnded(DisconnectCode code)
    {
        if (Server is not null)
        {
            _logger.LogDisconnectedLocal(code);

            // The player of a solo or host process left: the world is saved and the remote players get HostLeft (spec: Wyj�cie hosta).
            // During the shutdown the server itself ends this session, so only a running server is asked to stop.
            if (Server.RuntimeState is ServerRuntimeState.Running or ServerRuntimeState.SafeMode)
            {
                if (Quit is not null)
                {
                    Quit.RequestStop();
                }
                else
                {
                    RequestShutdown();
                }
            }

            return;
        }

        _clientEnded = true;
        _clientExitCode = code is DisconnectCode.Normal or DisconnectCode.HostLeft or DisconnectCode.ServerShutdown ? 0 : 1;
        Quit?.RequestStop();
    }

    private void OnClientRejected(HandshakeRejectCode code, string detail)
    {
        if (Server is not null)
        {
            return;
        }

        _env.Print($"CONNECTION REFUSED: {code} {detail}");
        _clientEnded = true;
        _clientExitCode = 5;
        Quit?.RequestStop();
    }
}

internal static partial class GameCompositionLog
{
    [LoggerMessage(EventId = 9201, Level = LogLevel.Information, Message = "the local client ended its session: {Code}")]
    internal static partial void LogDisconnectedLocal(this ILogger logger, DisconnectCode code);

    [LoggerMessage(EventId = 9202, Level = LogLevel.Error, Message = "the start failed after the server was running; shutting the server down")]
    internal static partial void LogStartFailedAfterRunning(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9203, Level = LogLevel.Warning, Message = "last-world.txt could not be written; the server keeps running")]
    internal static partial void LogLastWorldNotWritten(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9204, Level = LogLevel.Warning, Message = "disposing the client transport failed")]
    internal static partial void LogTransportDisposeFailed(this ILogger logger, Exception exception);
}
