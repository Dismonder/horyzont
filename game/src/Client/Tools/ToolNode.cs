using Godot;

using Horyzont.Client;
using Horyzont.Client.Session;
using Horyzont.Client.Ships;
using Horyzont.Client.Tools;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Terrain;
using Horyzont.Net.Session;
using Horyzont.Server;

using Microsoft.Extensions.Logging;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Client.Tools;

/// <summary>
/// The tools of the local player of a solo or host process (K2-13), a child of <c>/Root/Client</c>: it turns the input of a physics tick
/// into one <see cref="ToolController.Update"/> from the aim of the local player, hands the replies of the server to the
/// <see cref="PendingIntentTracker"/> and keeps the preview that the visuals draw. The controller is created when the server hello
/// arrives (its palettes come from the registries of the hello) and dropped when the session ends. The edit prediction is
/// <see cref="NoEditPrediction"/>: the terrain and the constructs are shared with the server (decision 7), so the result shows up
/// when the server has applied the operation, in the same process.
/// </summary>
public sealed partial class ToolNode : Node, IClientSessionListener
{
    /// <summary>Physics priority: after the client node (0) and the local player (10), so the aim uses the newest pose.</summary>
    public const int PhysicsPriority = 20;

    private const int ShipObserveEveryTicks = 6;

    private ClientRuntime? _client;
    private Func<ServerHello, IToolWorldView>? _views;
    private Func<IEditPrediction>? _predictionFactory;
    private IEditPrediction? _prediction;
    private ILocalPlayerView? _player;
    private GameRules _rules = new();
    private ILoggerFactory? _loggers;
    private ILogger<ToolNode>? _logger;
    private IToolWorldView? _view;
    private ShipModeSwitch? _ship;
    private long _tick;
    private int _accepted;
    private int _rejected;

    /// <summary>Raised on the main thread when the server answered an intent that the tracker knew (the HUD and tests listen).</summary>
    public event Action<IntentReply, PendingIntentOutcome>? IntentAnswered;

    /// <summary>Gets or sets where the input of each tick comes from (the keyboard and mouse reader; tests script it).</summary>
    public IToolInputSource InputSource { get; set; } = new NoToolInput();

    /// <summary>Gets the controller; null before the server hello and after the session ended.</summary>
    public ToolController? Controller { get; private set; }

    /// <summary>Gets the tracker of the intents that wait for the server; null while there is no controller.</summary>
    public PendingIntentTracker? Pending { get; private set; }

    /// <summary>Gets the view of the world that the tools aim at; null while there is no controller.</summary>
    public IToolWorldView? World => _view;

    /// <summary>
    /// Gets or sets the block definitions of the game content (mass and function by key) that the ship mode key and the developer HUD
    /// read; set before the server hello. Without it every block counts as plain and the key finds no cockpit.
    /// </summary>
    public IReadOnlyDictionary<string, BlockTypeDef>? BlockDefinitions { get; set; }

    /// <summary>Gets the ship mode key handler (K5-23); null while there is no controller.</summary>
    public ShipModeSwitch? Ship => _ship;

    /// <summary>Gets the result of the latest press of the ship mode key.</summary>
    public ShipModeToggleResult? LastShipToggle { get; private set; }

    /// <summary>Gets the physics tick of the latest press of the ship mode key (the HUD shows its result for a short time).</summary>
    public long LastShipToggleTick { get; private set; }

    /// <summary>Gets the block palette of the server hello; null before it.</summary>
    public BlockPalette? Blocks { get; private set; }

    /// <summary>Gets the material palette of the server hello; null before it.</summary>
    public MaterialPalette? Materials { get; private set; }

    /// <summary>Gets the planet of the local player (the frame of <see cref="Preview"/>).</summary>
    public Horyzont.Core.Ids.PlanetId Planet => _player?.Planet ?? default;

    /// <summary>Gets the current tool settings (defaults before the hello).</summary>
    public ToolSettings? Settings => Controller?.Settings;

    /// <summary>Gets the preview of the latest tick.</summary>
    public ToolPreview Preview => Controller?.Preview ?? default;

    /// <summary>Gets the number of intents the server accepted since the hello.</summary>
    public int Accepted => _accepted;

    /// <summary>Gets the number of intents the server refused since the hello.</summary>
    public int Rejected => _rejected;

    /// <summary>Gets the latest answer of the server to an intent of the tools.</summary>
    public IntentReply? LastReply { get; private set; }

    /// <summary>Gets the number of physics ticks the node has run.</summary>
    public long Ticks => _tick;

    /// <summary>Gives the node its dependencies; call before it enters the tree.</summary>
    /// <param name="client">The client runtime.</param>
    /// <param name="server">The server runtime of the same process.</param>
    /// <param name="terrain">The terrain service of the server (resolved at the hello, the terrain does not exist earlier).</param>
    /// <param name="player">The local player (feet, camera).</param>
    /// <param name="rules">The rules of the game, the same as the server uses.</param>
    /// <param name="loggers">Loggers.</param>
    public void Initialize(
        ClientRuntime client, ServerRuntime server, Func<ITerrainService> terrain, ILocalPlayerView player, GameRules rules, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(terrain);
        Initialize(client, hello => new GodotToolWorldView(server.State, terrain(), hello.YourPlayer),
            () => NoEditPrediction.Instance, player, rules, loggers);
    }

    /// <summary>Gives remote or local tools their world and prediction factories before entering the tree.</summary>
    /// <param name="client">Client runtime.</param>
    /// <param name="views">World view resolved from each server hello.</param>
    /// <param name="prediction">Prediction created for each session.</param>
    /// <param name="player">Local player view.</param>
    /// <param name="rules">Game rules.</param>
    /// <param name="loggers">Loggers.</param>
    public void Initialize(
        ClientRuntime client, Func<ServerHello, IToolWorldView> views, Func<IEditPrediction> prediction,
        ILocalPlayerView player, GameRules rules, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(prediction);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(loggers);
        _client = client;
        _views = views;
        _predictionFactory = prediction;
        _player = player;
        _rules = rules;
        _loggers = loggers;
        _logger = loggers.CreateLogger<ToolNode>();
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        ProcessPhysicsPriority = PhysicsPriority;
        if (_client is null)
        {
            throw new InvalidOperationException("ToolNode.Initialize was not called before the node entered the tree.");
        }

        if (InputSource is NoToolInput && GetNodeOrNull<ToolInputReader>("Input") is { } reader)
        {
            InputSource = reader;
        }

        _client.AddListener(this);
    }

    /// <inheritdoc />
    public override void _ExitTree() => _client?.RemoveListener(this);

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        _view = _views!(hello);
        _prediction = _predictionFactory!();
        Pending = new PendingIntentTracker(_prediction, _loggers!.CreateLogger<PendingIntentTracker>());
        Blocks = BlockPalette.FromHello(hello);
        Materials = MaterialPalette.FromHello(hello);
        Controller = new ToolController(
            _rules, _view, new SessionIntentSender(_client!.Session), Pending, Blocks, Materials, _loggers!.CreateLogger<ToolController>());
        var content = BlockContentTable.Create(hello.BlockTypes, BlockDefinitions ?? new Dictionary<string, BlockTypeDef>());
        _ship = new ShipModeSwitch(_view, new SessionIntentSender(_client.Session), Pending, content, _rules);
        LastShipToggle = null;
        _accepted = 0;
        _rejected = 0;
        LastReply = null;
    }

    /// <inheritdoc />
    public void OnIntentReply(IntentReply reply)
    {
        if (_prediction is IIntentReplyObserver observer)
        {
            observer.NoteReply(reply);
        }

        if (Pending is null || !Pending.OnReply(reply, out var outcome))
        {
            return;
        }

        LastReply = reply;
        if (outcome == PendingIntentOutcome.Accepted)
        {
            _accepted++;
        }
        else
        {
            _rejected++;
        }

        IntentAnswered?.Invoke(reply, outcome);
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code)
    {
        // Revert from the newest: the contract of the prediction, although NoEditPrediction has nothing to undo.
        Pending?.Clear();
        Controller = null;
        _ship = null;
        Pending = null;
        _view = null;
        _prediction = null;
    }

    /// <inheritdoc />
    public override void _PhysicsProcess(double delta)
    {
        var tick = ++_tick;
        var frame = InputSource.Take();
        if (Controller is not { } controller)
        {
            return;
        }

        if (frame.RadiusSteps != 0 && controller.Settings.Tool == ToolKind.Builder)
        {
            frame = frame with { PaletteSteps = frame.RadiusSteps, RadiusSteps = 0 };
        }

        var player = _player!;
        // An aim that is not usable (default) still applies the setting steps and clears the preview; no intent is sent.
        if (_client!.Session.State != ClientState.InGame || !player.IsActive || player.CameraMode == CameraMode.DevFree)
        {
            controller.Update(frame, default, tick);
            _ship?.ClearAim();
            return;
        }

        var forward = NumVector3.Transform(-NumVector3.UnitZ, player.BodyRotation);
        if (!AimRayBuilder.TryBuild(player.Planet, player.PredictedFeet, player.CameraOrigin, player.CameraDirection, forward, _view!, _rules, out var ray))
        {
            controller.Update(frame, default, tick);
            _ship?.ClearAim();
            return;
        }

        controller.Update(frame, ray, tick);
        if (_ship is { } ship)
        {
            // The raycast for the developer HUD runs at 10 Hz; a press of the key is handled at once.
            if (frame.ShipModeToggle)
            {
                LastShipToggle = ship.Toggle(ray, tick);
                LastShipToggleTick = tick;
            }

            if (tick % ShipObserveEveryTicks == 0)
            {
                ship.Observe(ray);
            }
        }
    }

    private sealed class NoToolInput : IToolInputSource
    {
        public ToolInputFrame Take() => default;
    }
}
