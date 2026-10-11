using Godot;

using Horyzont.Client.Frames;
using Horyzont.Client.Replication;
using Horyzont.Client.Ships;
using Horyzont.Core.Ids;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Space;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Client.Character;

namespace Horyzont.Game.Client.Ships;

/// <summary>
/// Flight HUD (K5-24 decision 20): shown while the own body is attached to a construct in the ship mode; altitude over the nearest
/// body (client frame graph and star system), speed from the frame history, the limit of <see cref="FlightRules"/>, fuel with the
/// warnings and the launch block from <see cref="IClientShipStatus"/> (dashes until K5-26). Labels come from <c>tr()</c> once; the
/// text is written into a reused buffer.
/// </summary>
public sealed partial class FlightHud : CanvasLayer
{
    private const int MaxBodies = 16;

    private readonly LocalPlayer _player;
    private readonly ClientFrames _frames;
    private readonly ConstructMirror? _constructs;
    private readonly Func<StarSystemModel?> _system;
    private readonly IClientShipStatus _status;
    private readonly char[] _buffer = new char[512];
    private readonly HudBody[] _bodies = new HudBody[MaxBodies];
    private readonly FlightRules _rules = FlightRules.Default;
    private FlightHudLabels? _labels;
    private Label? _label;
    private int _lastLength = -1;

    /// <summary>Initializes a new instance of the <see cref="FlightHud"/> class.</summary>
    /// <param name="player">The local player (attached frame).</param>
    /// <param name="frames">The client frames.</param>
    /// <param name="constructs">The construct mirror (mode); null without constructs.</param>
    /// <param name="system">The star system model of the session; null before it arrives.</param>
    /// <param name="status">Ship status.</param>
    public FlightHud(LocalPlayer player, ClientFrames frames, ConstructMirror? constructs, Func<StarSystemModel?> system, IClientShipStatus status)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _constructs = constructs;
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        Name = "FlightHud";
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _labels = new FlightHudLabels(
            Tr("FLIGHT_ALTITUDE"), Tr("FLIGHT_SPEED"), Tr("FLIGHT_LIMIT"), Tr("FLIGHT_FUEL"), Tr("FLIGHT_FUEL_LOW"), Tr("FLIGHT_FUEL_RESERVE"),
            Tr("FLIGHT_BLOCKED_RESERVE"), Tr("FLIGHT_BLOCKED_PERSON"), Tr("FLIGHT_TERRAIN_UNAVAILABLE"), Tr("FLIGHT_TERRAIN_HOLD"));
        _label = new Label { Position = new Vector2(16, 16), Visible = false };
        AddChild(_label);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_label is null || _labels is null)
        {
            return;
        }

        if (!_player.TryGetAttached(out var frame, out var tick) || frame.Kind != FrameKind.Construct || _frames.Graph is not { } graph
            || _constructs is null || !_constructs.TryGetConstruct(new EntityId(frame.Id), out var ship) || ship.Mode != ConstructMode.Ship
            || !graph.TryGetWorldPose(frame, tick, out var now) || !graph.TryGetWorldPose(frame, tick - 1, out var before))
        {
            _label.Visible = false;
            return;
        }

        var count = 0;
        if (_system() is { } model)
        {
            for (var i = 0; i < model.BodyCount && count < MaxBodies; i++)
            {
                var body = model.GetBody(i);
                if (graph.TryGetWorldPose(FrameRef.Of(body.Planet), tick, out var center))
                {
                    _bodies[count++] = new HudBody(center.Position, body.RadiusMeters);
                }
            }
        }

        var hasStatus = _status.TryGet(ship.Id, out var status);
        var values = FlightHudValues.Compute(
            now.Position, before.Position, 1.0 / Horyzont.Server.GameServerHooks.TickRate, _bodies.AsSpan(0, count), hasStatus, status, _rules);
        var length = FlightHudText.Format(_buffer, values, _labels);
        if (length != _lastLength || !_buffer.AsSpan(0, length).SequenceEqual(_label.Text.AsSpan()))
        {
            _label.Text = new string(_buffer, 0, length);
            _lastLength = length;
        }

        _label.Visible = true;
    }
}
