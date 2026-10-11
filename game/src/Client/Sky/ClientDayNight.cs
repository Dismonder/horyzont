using Godot;

using Horyzont.Client;
using Horyzont.Client.Session;
using Horyzont.Client.Space;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Game.Frames;
using Horyzont.Game.Planet.Sky;

namespace Horyzont.Game.Client.Sky;

/// <summary>
/// The sun and the sky of the client (decision D12). The direction of the sun is a pure function of the server tick
/// (<see cref="DayNightCycle.SunDirection(ulong, int, float)"/>); this node only estimates that tick and sets the <see cref="SunLight"/>: in a solo or
/// host process it is the tick of the server itself, for a remote client the tick of the hello plus the ticks since, corrected by
/// every own-state snapshot. The client has no clock of day of its own. The sky follows: it is turned so its zenith is the local
/// up of the camera, and it fades with the sun below the horizon and with height above the atmosphere. Lighting only; nothing
/// here reaches the simulation or a save.
/// </summary>
public sealed partial class ClientDayNight : Node, IClientSessionListener
{
    private readonly ILocalPlayerView? _view;
    private readonly Func<ulong?>? _authoritativeTick;
    private readonly bool _render;
    private ClientRuntime? _runtime;
    private SunLight? _sun;
    private Godot.Environment? _environment;
    private ProceduralSkyMaterial? _sky;
    private PlanetParams? _planet;
    private int _tickRate = 60;
    private ulong _tick;

    /// <summary>Gets or sets the star system of the session (K5-21): its sun cycle drives the sun; null = the day of the home planet.</summary>
    public ClientStarSystem? StarSystem { get; set; }

    /// <summary>Gets or sets the frame nodes of the client (K5-20): planet-frame values go through the planet node; null = identity.</summary>
    public FrameNodes? FrameNodes { get; set; }

    /// <summary>Creates the node.</summary>
    /// <param name="view">The local player, for the up and the height of the camera; null for none.</param>
    /// <param name="authoritativeTick">The tick of the server in the same process (solo and host); null for a remote client.</param>
    /// <param name="render">Whether sky and shadows are created (a headless process only needs the sun direction).</param>
    public ClientDayNight(ILocalPlayerView? view, Func<ulong?>? authoritativeTick, bool render)
    {
        _view = view;
        _authoritativeTick = authoritativeTick;
        _render = render;
        Name = "DayNight";
    }

    /// <summary>Gets the sun light node.</summary>
    public SunLight Sun => _sun ?? throw new InvalidOperationException("ClientDayNight is not in the tree yet.");

    /// <summary>Gets the tick the sun was last set for; 0 before the hello.</summary>
    public ulong AppliedTick { get; private set; }

    /// <summary>Gets the share of daylight (0 night, 1 day) at the camera.</summary>
    public float Daylight { get; private set; } = 1f;

    /// <summary>Gives the node the runtime whose hello and snapshots it follows; call before the node enters the tree.</summary>
    /// <param name="runtime">The client runtime.</param>
    public void Initialize(ClientRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (_runtime is null)
        {
            throw new InvalidOperationException("ClientDayNight.Initialize was not called before the node entered the tree.");
        }

        _sun = new SunLight { Name = "Sun", ShadowEnabled = _render, DirectionalShadowMaxDistance = 300f, LightEnergy = 1.2f };
        AddChild(_sun);
        if (_render)
        {
            _sky = new ProceduralSkyMaterial();
            _environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Godot.Sky { SkyMaterial = _sky },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            };
            AddChild(new WorldEnvironment { Name = "Environment", Environment = _environment });
        }

        _runtime.AddListener(this);
    }

    /// <inheritdoc />
    public override void _ExitTree() => _runtime?.RemoveListener(this);

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello)
    {
        _planet = hello.Planets.Count > 0 ? hello.Planets[0] : null;
        _tickRate = hello.TickRate;
        _tick = hello.ServerTick.Value;
    }

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        if (message is Snapshot snapshot && _authoritativeTick is null)
        {
            _tick = snapshot.ServerTick.Value;
        }
    }

    /// <inheritdoc />
    public override void _PhysicsProcess(double delta)
    {
        if (_planet is null || _sun is null)
        {
            return;
        }

        _tick = _authoritativeTick?.Invoke() ?? _tick + 1;
        AppliedTick = _tick;
        _sun.Apply(_tick, _tickRate, SunCycleSeconds);
        UpdateSky();
    }

    // K5-21 decision 17: the cycle of the star system when known (equal to the home day in worlds of V1), else the home planet day.
    private float SunCycleSeconds => StarSystem is { SunCycleSeconds: > 0f } system ? system.SunCycleSeconds : _planet!.DayLengthSeconds;

    private void UpdateSky()
    {
        var planet = _planet!;
        var origin = _view is { IsActive: true } ? _view.CameraOrigin : System.Numerics.Vector3.UnitX * planet.RadiusMeters;
        var length = origin.Length();
        var up = length > 1e-3f ? origin / length : System.Numerics.Vector3.UnitY;
        var sun = DayNightCycle.SunDirection(_tick, _tickRate, SunCycleSeconds);
        var elevation = System.Numerics.Vector3.Dot(sun, up);
        Daylight = Mathf.SmoothStep(-0.12f, 0.2f, elevation);
        if (_environment is null || _sky is null)
        {
            return;
        }

        var altitude = length - planet.RadiusMeters;
        var atmosphere = 1f - Mathf.SmoothStep(planet.AtmosphereHeightMeters * 0.5f, planet.AtmosphereHeightMeters, altitude);
        var skyEnergy = Mathf.Lerp(0.02f, 1f, Daylight) * atmosphere;
        _environment.BackgroundEnergyMultiplier = skyEnergy;
        _environment.AmbientLightEnergy = Mathf.Lerp(0.04f, 1f, Daylight);
        _sun!.LightEnergy = 1.4f * Daylight;
        _environment.SkyRotation = new Basis(new Quaternion(Vector3.Up, FrameNodes.PlanetSpaceOf(planet.Id).ToGodotDirection(up))).GetEuler();
    }
}
