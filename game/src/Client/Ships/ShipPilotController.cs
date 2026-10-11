using Godot;

using Horyzont.Client.Frames;
using Horyzont.Client.Replication;
using Horyzont.Client.Session;
using Horyzont.Client.Ships;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Net.Session;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Client.Ships;

/// <summary>
/// The pilot of the local player (K5-25 decisions 16, 18, 23): while the own seat is the control cockpit of a ship in the ship mode, each
/// physics tick reads the pilot keys and the mouse (<see cref="ShipPilotInput"/>), steps <see cref="ShipPilot"/> (prediction and
/// <see cref="ShipInputBatch"/>) and feeds it the corrections and the zone transitions of the ship. The environment follows the parent
/// frame of the ship (<see cref="ClientFlightEnvironment.For"/>). The client sweep is free (no client hull collider yet — the server
/// contact arrives in the corrections and is smoothed below 5 m). The own seat comes from <see cref="IOwnSeatSource"/>; on a navigator
/// seat a HUD hint names the map key (K5-27 not merged).
/// </summary>
public sealed partial class ShipPilotController : Node, IClientSessionListener
{
    private static readonly StringName _forward = new("ship_thrust_forward");
    private static readonly StringName _back = new("ship_thrust_back");
    private static readonly StringName _left = new("ship_thrust_left");
    private static readonly StringName _right = new("ship_thrust_right");
    private static readonly StringName _up = new("ship_thrust_up");
    private static readonly StringName _down = new("ship_thrust_down");
    private static readonly StringName _rollLeft = new("ship_roll_left");
    private static readonly StringName _rollRight = new("ship_roll_right");
    private static readonly StringName _pulse = new("ship_pulse_toggle");
    private static readonly StringName _gear = new("ship_gear_toggle");
    private static readonly StringName _assist = new("ship_assist_toggle");

    private readonly ClientRuntime _client;
    private readonly IOwnSeatSource _seat;
    private readonly ConstructMirror _constructs;
    private readonly ClientFrames _frames;
    private readonly Func<StarSystemModel?> _system;
    private readonly IReadOnlyDictionary<string, BlockTypeDef> _definitions;
    private readonly bool _render;
    private readonly FlightRules _rules = FlightRules.Default;
    private readonly ShipPilot _pilot;
    private IReadOnlyList<PlanetParams> _planets = [];
    private BlockContentTable? _content;
    private ShipEnvironmentSource _source;
    private FrameRef _sourceParent;
    private FrameRef _parent;
    private EntityId _statsConstruct;
    private uint _statsVersion;
    private ShipStats? _stats;
    private Vector2 _mouse;
    private uint _tick;
    private Label? _hint;

    /// <summary>Initializes a new instance of the <see cref="ShipPilotController"/> class.</summary>
    /// <param name="client">The client runtime.</param>
    /// <param name="seat">The own seat.</param>
    /// <param name="constructs">The construct mirror.</param>
    /// <param name="frames">The client frames.</param>
    /// <param name="system">The star system model; null before it arrives.</param>
    /// <param name="definitions">Block definitions by key (mass, function) for the ship stats.</param>
    /// <param name="render">Whether the HUD hint is drawn.</param>
    /// <param name="tickRate">Ticks per second (the server rate).</param>
    /// <param name="loggers">Loggers.</param>
    public ShipPilotController(
        ClientRuntime client, IOwnSeatSource seat, ConstructMirror constructs, ClientFrames frames, Func<StarSystemModel?> system,
        IReadOnlyDictionary<string, BlockTypeDef> definitions, bool render, int tickRate, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _seat = seat ?? throw new ArgumentNullException(nameof(seat));
        _constructs = constructs ?? throw new ArgumentNullException(nameof(constructs));
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _render = render;
        _source = ClientFlightEnvironment.For(default, null, null, _rules);
        var prediction = new ShipPrediction(
            (in ShipMotion motion, ShipStats stats) => _source(motion, stats),
            static (in PoseD _, in PoseD _) => ShipSweepResult.Free,
            _rules,
            tickRate,
            loggers.CreateLogger<ShipPrediction>());
        _pilot = new ShipPilot(prediction, Send);
        Name = "ShipPilot";
    }

    /// <summary>Gets the pilot loop (prediction, pose override of the own ship — R7).</summary>
    public ShipPilot Pilot => _pilot;

    /// <summary>Gets the role of the own seat; <see cref="SeatRole.None"/> when standing.</summary>
    public SeatRole Role { get; private set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (_render)
        {
            _hint = new Label { Position = new Vector2(16, 120), Visible = false, Text = Tr("SEAT_NAVIGATOR_MAP") };
            var layer = new CanvasLayer { Name = "SeatHint" };
            layer.AddChild(_hint);
            AddChild(layer);
        }
    }

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        _planets = hello.Planets;
        _content = BlockContentTable.Create(hello.BlockTypes, _definitions);
        _stats = null;
        _pilot.End();
    }

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        switch (message)
        {
            case ShipCorrection correction:
                _pilot.OnCorrection(correction);
                break;
            case OperationApplied { Operation: EntityReparentOp op } applied when op.Entity == _pilot.Ship && _pilot.Ship != default:
                Rebase(op, applied.Tick.Value);
                break;
        }
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code) => _pilot.End();

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && _pilot.Ship != default && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _mouse += motion.Relative;
        }
    }

    /// <inheritdoc />
    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        if (_client.Session.State != ClientState.InGame || !TryGetPilotSeat(out var ship, out var stats))
        {
            _pilot.End();
            _mouse = Vector2.Zero;
            return;
        }

        _pilot.Begin(ship.Id, stats);
        _parent = ship.Parent;
        if (_sourceParent != ship.Parent)
        {
            _sourceParent = ship.Parent;
            var planet = ship.Parent.Kind == FrameKind.Planet ? FindPlanet(new PlanetId(ship.Parent.Id)) : null;
            _source = ClientFlightEnvironment.For(ship.Parent, planet, _system(), _rules);
        }

        var reading = Input.MouseMode == Input.MouseModeEnum.Captured || DisplayServer.GetName() == "headless";
        var control = reading
            ? _pilot.Input.Build(
                Input.GetActionStrength(_right) - Input.GetActionStrength(_left),
                Input.GetActionStrength(_up) - Input.GetActionStrength(_down),
                Input.GetActionStrength(_forward) - Input.GetActionStrength(_back),
                Input.GetActionStrength(_rollRight) - Input.GetActionStrength(_rollLeft),
                (float)_mouse.X,
                (float)_mouse.Y,
                Input.IsActionJustPressed(_pulse),
                Input.IsActionJustPressed(_gear),
                Input.IsActionJustPressed(_assist))
            : _pilot.Input.Build(0, 0, 0, 0, 0, 0, false, false, false);
        _mouse = Vector2.Zero;
        _pilot.Tick(_tick, control);
    }

    private bool TryGetPilotSeat(out Construct ship, out ShipStats stats)
    {
        ship = null!;
        stats = null!;
        Role = SeatRole.None;
        if (!_seat.TryGetOwnSeat(out var id, out var cell) || _content is null || !_constructs.TryGetConstruct(id, out var construct))
        {
            UpdateHint();
            return false;
        }

        if (_stats is null || _statsConstruct != id || _statsVersion != construct.Version)
        {
            _stats = ShipStatsCalculator.Compute(construct, _content);
            _statsConstruct = id;
            _statsVersion = construct.Version;
        }

        foreach (var seat in _stats.Seats)
        {
            if (seat.Cell == cell)
            {
                Role = seat.Role;
            }
        }

        UpdateHint();
        if (construct.Mode != ConstructMode.Ship || _stats.ControlCell != cell)
        {
            return false;
        }

        ship = construct;
        stats = _stats;
        return true;
    }

    private void UpdateHint()
    {
        if (_hint is not null)
        {
            _hint.Visible = Role == SeatRole.Navigator;
        }
    }

    private PlanetParams? FindPlanet(PlanetId id)
    {
        foreach (var planet in _planets)
        {
            if (planet.Id == id)
            {
                return planet;
            }
        }

        return null;
    }

    private void Rebase(EntityReparentOp op, ulong tick)
    {
        var graph = _frames.Graph;
        if (graph is not null && graph.TryGetWorldPose(_parent, tick, out var from) && graph.TryGetWorldPose(op.NewParent, tick, out var to))
        {
            // EA frames share their axes: the old origin seen from the new frame is a translation.
            _pilot.OnShipReparent(op.Entity, op.NewParentSeq, from.Position - to.Position);
        }
        else
        {
            _pilot.Prediction.Clear();
        }

        _parent = op.NewParent;
        _sourceParent = default;
    }

    private void Send(uint first, IReadOnlyList<ShipInput> inputs)
    {
        if (_client.Session.State == ClientState.InGame)
        {
            // Encoded inside the call; the list is the reused window of the sequencer.
            _client.Session.Send(new ShipInputBatch(first, inputs));
        }
    }
}
