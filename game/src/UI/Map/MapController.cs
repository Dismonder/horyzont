using Godot;

using Horyzont.Client.Markers;
using Horyzont.Client.Navigation;
using Horyzont.Client.Replication;
using Horyzont.Client.Session;
using Horyzont.Client.Space;
using Horyzont.Client.Tools;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.UI.Map;

/// <summary>
/// Owns the map screens of a session (K5-27 decision 12): the system map, the planet map and the marker dialog, the action
/// <c>toggle_map</c> and the <see cref="MapInputGate"/>. Opening releases the mouse and blocks movement and tools; closing (the same key
/// or Escape) gives the mouse back. The navigator seat (K5-25) opens the map through <see cref="IMapOpener"/>.
/// </summary>
public sealed partial class MapController : CanvasLayer, IMapOpener, IClientSessionListener
{
    private static readonly StringName _toggle = new("toggle_map");
    private static readonly StringName _cancel = new("ui_cancel");
    private static readonly StringName _release = new("release_mouse");

    private readonly MapInputGate _gate;
    private readonly PlanetMapCache _cache = new();
    private readonly List<NumVector3> _others = [];
    private readonly NumVector3[] _feet = new NumVector3[64];
    private IReadOnlyList<PlanetParams> _planets = [];
    private PlayerId _player;
    private ClientStarSystem _stars = null!;
    private IWorldPoseSource _poses = null!;
    private ClientMarkerStore _markers = null!;
    private EntityMirror? _entities;
    private INavigationViewpoint _viewpoint = null!;
    private Func<double> _renderTick = null!;
    private Func<bool> _canOpen = null!;
    private Action _onClosed = null!;
    private SystemMapPanel _system = null!;
    private PlanetMapPanel _planetPanel = null!;
    private MarkerEditDialog _dialog = null!;
    private Button _back = null!;
    private PlanetId? _planet;
    private int _markersVersion = -1;
    private double _sinceRefresh;

    /// <summary>Creates the controller.</summary>
    /// <param name="gate">The gate the controller opens and closes.</param>
    public MapController(MapInputGate gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        _gate = gate;
        Name = "MapController";
        Layer = 15;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Gets a value indicating whether the map is open.</summary>
    public bool IsOpen => _gate.IsOpen;

    /// <summary>Gets the system map panel (tests).</summary>
    public SystemMapPanel SystemPanel => _system;

    /// <summary>Gets the planet map panel (tests).</summary>
    public PlanetMapPanel PlanetPanel => _planetPanel;

    /// <summary>Gets the marker dialog (tests).</summary>
    public MarkerEditDialog Dialog => _dialog;

    /// <summary>Gives the controller its dependencies; call before it enters the tree.</summary>
    /// <param name="stars">The star system of the client.</param>
    /// <param name="poses">World poses of frames.</param>
    /// <param name="markers">Markers of the client.</param>
    /// <param name="entities">Replicated entities, if any.</param>
    /// <param name="viewpoint">Own position in the world.</param>
    /// <param name="navigation">The navigation target.</param>
    /// <param name="sender">Where the marker intents go.</param>
    /// <param name="renderTick">Server tick being rendered.</param>
    /// <param name="canOpen">Whether the map may open now (the menu is not on screen and a world is running).</param>
    /// <param name="onClosed">Called after the map closed (the game takes the mouse again).</param>
    public void Initialize(
        ClientStarSystem stars, IWorldPoseSource poses, ClientMarkerStore markers, EntityMirror? entities, INavigationViewpoint viewpoint,
        NavigationCoordinator navigation, IIntentSender sender, Func<double> renderTick, Func<bool> canOpen, Action onClosed)
    {
        _stars = stars;
        _poses = poses;
        _markers = markers;
        _entities = entities;
        _viewpoint = viewpoint;
        _renderTick = renderTick;
        _canOpen = canOpen;
        _onClosed = onClosed;
        _dialog = new MarkerEditDialog(sender, markers, navigation);
        _dialog.Closed += () => _markersVersion = -1;
    }

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        _planets = hello.Planets;
        _player = hello.YourPlayer;
    }

    /// <inheritdoc />
    public void OnDisconnected(Horyzont.Core.Protocol.DisconnectCode code) => Close();

    /// <inheritdoc />
    public override void _Ready()
    {
        _system = new SystemMapPanel { Name = "SystemMap" };
        _system.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _planetPanel = new PlanetMapPanel { Name = "PlanetMap", Visible = false };
        _planetPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _back = new Button { Name = "Back", Text = "MAP_SYSTEM", Visible = false };
        _back.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _back.Pressed += ShowSystem;
        var shade = new ColorRect { Name = "Shade", Color = new Color(0f, 0f, 0.03f, 0.92f), MouseFilter = Control.MouseFilterEnum.Stop };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);
        AddChild(_system);
        AddChild(_planetPanel);
        AddChild(_back);
        AddChild(_dialog);
        _system.BodyClicked += ShowPlanet;
        _system.MarkerClicked += id =>
        {
            if (_markers.TryGet(id, out var marker))
            {
                _dialog.OpenEdit(marker);
            }
        };
        _planetPanel.SurfaceClicked += (planet, position) => _dialog.OpenNew(planet, position);
        _planetPanel.MarkerClicked += _dialog.OpenEdit;
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event.IsEcho())
        {
            return;
        }

        if (@event.IsActionPressed(_toggle))
        {
            if (_gate.IsOpen || _canOpen())
            {
                Toggle();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (_gate.IsOpen && (@event.IsActionPressed(_cancel) || @event.IsActionPressed(_release)))
        {
            if (_dialog.Visible)
            {
                _dialog.Close();
            }
            else
            {
                Close();
            }

            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!_gate.IsOpen)
        {
            return;
        }

        _sinceRefresh += delta;
        if (_sinceRefresh < 0.25 && _markersVersion == _markers.Version)
        {
            return;
        }

        _sinceRefresh = 0;
        _markersVersion = _markers.Version;
        Refresh();
    }

    /// <summary>Opens the system map.</summary>
    public void Open()
    {
        if (_gate.IsOpen)
        {
            return;
        }

        _gate.IsOpen = true;
        Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        ShowSystem();
        Refresh();
    }

    /// <summary>Closes the map and gives the mouse back to the game.</summary>
    public void Close()
    {
        if (!_gate.IsOpen)
        {
            return;
        }

        _dialog.Close();
        _gate.IsOpen = false;
        Visible = false;
        _onClosed();
    }

    private void Toggle()
    {
        if (_gate.IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    private void ShowSystem()
    {
        _planet = null;
        _system.Visible = true;
        _planetPanel.Visible = false;
        _back.Visible = false;
        Refresh();
    }

    private void ShowPlanet(PlanetId planet)
    {
        foreach (var candidate in _planets)
        {
            if (candidate.Id == planet)
            {
                _planet = planet;
                _planetPanel.ShowMap(_cache.Get(candidate));
                _system.Visible = false;
                _planetPanel.Visible = true;
                _back.Visible = true;
                Refresh();
                return;
            }
        }
    }

    private void Refresh()
    {
        var tick = _renderTick();
        if (_planet is not { } planet)
        {
            _system.SetModel(SystemMapModel.Build(
                _stars, _poses, _markers, _entities, _viewpoint, (ulong)Math.Max(0, tick), 60, tick,
                (_, owner, kind) => owner is { } id && id == _player ? null : owner is { } other ? "#" + other.Value : null));
            return;
        }

        var markers = new List<Core.World.Markers.MarkerRecord>();
        foreach (var marker in _markers.All)
        {
            if (marker.Planet == planet)
            {
                markers.Add(marker);
            }
        }

        _others.Clear();
        if (_entities is not null)
        {
            var count = _entities.CopyOtherPlayerFeet(planet, _player, _feet);
            for (var i = 0; i < count; i++)
            {
                _others.Add(_feet[i]);
            }
        }

        NumVector3? own = null;
        if (_viewpoint.TryGetWorldPosition(tick, out var world) && _poses.TryGetWorldPose(FrameRef.Of(planet), tick, out var pose))
        {
            var local = FrameMath.InverseTransformPoint(pose, world);
            own = new NumVector3((float)local.X, (float)local.Y, (float)local.Z);
        }

        _planetPanel.SetContents(markers, _others, own);
    }
}
