using Godot;

using Horyzont.Client;
using Horyzont.Client.Camera;
using Horyzont.Client.Movement;
using Horyzont.Client.Replication;
using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Character;
using Horyzont.Game.Frames;
using Horyzont.Game.Physics;
using Horyzont.Game.Server.Characters;

using Horyzont.Net.Session;

using Microsoft.Extensions.Logging;

using NumQuaternion = System.Numerics.Quaternion;
using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Client.Character;

/// <summary>
/// The local player (K2-11): once per physics tick it reads the input, predicts the own character with the same
/// <see cref="CharacterMotor"/> and engine physics as the server, sends the inputs, compares the own-character snapshots of the
/// server with its predictions and, on a mismatch, goes back to the server state and replays the inputs the server has not yet
/// processed. It also places the camera and the terrain viewers and is the <see cref="ILocalPlayerView"/> for the tools of K2-13.
/// Runs after <c>/Root/Client</c> polled the session (priority <see cref="PhysicsPriority"/>), so the snapshots of this frame are in.
/// </summary>
public sealed partial class LocalPlayer : Node, ILocalPlayerView, IClientSessionListener, IFullSnapshotSink, ITerrainViewerSource
{
    /// <summary>Physics priority: after the client node (0) and the server node (-100), before the tools of K2-13.</summary>
    public const int PhysicsPriority = 10;

    /// <summary>How far under the body the client looks for terrain collision before it predicts in the air (metres).</summary>
    public const float GateDepthMeters = 16f;

    private readonly InputSequencer _sequencer = new();
    private readonly PredictionEntry[] _replay = new PredictionEntry[PredictionBuffer.Capacity];
    private readonly PredictedState[] _replayStates = new PredictedState[PredictionBuffer.Capacity];
    private readonly CameraUpSmoother _smoother = new();
    private readonly PresentationCorrection _presentation = new();
    private uint _parentSeq;
    private bool _hasParent;
    private long _smallCorrections;
    private long _largeCorrections;
    private float _maxPresentationOffset;
    private OwnState _lastCorrection;
    private float _lastCorrectionPositionError;
    private float _lastCorrectionVelocityError;

    private ClientRuntime? _runtime;
    private ClientNode? _clientNode;
    private CameraRig? _rig;
    private DevFlyCamera? _dev;
    private ILoggerFactory? _loggers;
    private ILogger<LocalPlayer>? _logger;
    private GameRules _rules = new();
    private MovementRules _movement = new();
    private PredictionBuffer? _buffer;
    private OrientationTracker? _orientation;
    private CameraContinuityMonitor? _monitor;
    private OwnStateValidator? _validator;
    private ServerHello? _hello;
    private PlanetParams? _planet;
    private PlayerBody? _body;
    private GroundProbe? _probe;
    private IClientTerrainView? _terrain;
    private VoxelViewer? _visualViewer;
    private VoxelViewer? _collisionViewer;
    private Snapshot? _pending;
    private bool _render;
    private bool _devAllowed;
    private bool _devFlyOn;
    private int _devFlyCorrections;
    private float _lastYaw;
    private float _lastPitch;
    private bool _connected;
    private bool _snapCamera;
    private float _tickSeconds = 1f / 60f;
    private uint _clientTick;
    private uint _visualDistance;
    private NumVector3 _cameraOrigin;
    private NumVector3 _cameraDirection = -NumVector3.UnitZ;
    private CameraMode _cameraMode = CameraMode.ThirdPerson;
    private MovementMode _serverMode = MovementMode.Frozen;

    /// <summary>Gets or sets where the input of each tick comes from (the keyboard and mouse reader; tests script it).</summary>
    public ILocalInputSource InputSource { get; set; } = new NoInput();

    /// <summary>Gets or sets world readiness; K2 is always ready.</summary>
    public IClientWorldReadiness WorldReadiness { get; set; } = AlwaysReadyWorld.Instance;

    /// <inheritdoc />
    public bool TryGetViewers(out TerrainViewers viewers)
    {
        viewers = default;
        if (_planet is not { } planet || (_visualViewer is null && _collisionViewer is null))
        {
            return false;
        }

        var visual = _visualViewer;
        var collision = _collisionViewer;
        viewers = new TerrainViewers(
            planet.Id,
            visual is null ? default : PlanetFrames.ToPlanetLocal(planet.Id, visual.GlobalPosition), visual?.ViewDistance ?? 0,
            collision is null ? default : PlanetFrames.ToPlanetLocal(planet.Id, collision.GlobalPosition), collision?.ViewDistance ?? 0);
        return true;
    }

    /// <summary>Gets the number of corrections (a mismatch with the server) since the connection or the last <see cref="ResetStatistics"/>.</summary>
    public int Corrections { get; private set; }

    /// <summary>Gets the number of acknowledgements compared with a prediction since the last <see cref="ResetStatistics"/>.</summary>
    public int Compared { get; private set; }

    /// <summary>Gets a description of the latest correction (diagnostics and tests); empty when there was none.</summary>
    public string LastCorrection => Corrections == 0 ? string.Empty : string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"seq {_lastCorrection.LastProcessedInputSeq}: position error {_lastCorrectionPositionError * 1000f:F2} mm, velocity error {_lastCorrectionVelocityError:F3} m/s, server feet {_lastCorrection.Feet} velocity {_lastCorrection.Velocity}");

    /// <summary>Gets immutable motion counters for tests and the K4 performance gate.</summary>
    public MotionStatistics Motion => new(_smallCorrections, _largeCorrections, _maxPresentationOffset,
        RemoteMotion?.SampleUnderruns ?? 0, 0.1);

    /// <summary>Gets or sets the session's remote full-snapshot sink, supplied by composition.</summary>
    public RemoteMotionSampleAdapter? RemoteMotion { get; set; }

    /// <summary>Gets the largest position error (metres) of an acknowledgement that matched.</summary>
    public float MaxAckPositionError { get; private set; }

    /// <summary>Gets the number of camera jumps the continuity monitor saw (corrections and dev camera changes are declared snaps).</summary>
    public int CameraJumps => _monitor?.JumpCount ?? 0;

    /// <summary>Gets the latest server movement mode.</summary>
    public MovementMode ServerMode => _serverMode;

    /// <summary>Gets the body of the predicted character; null before the first server state.</summary>
    public PlayerBody? Body => _body;

    /// <summary>Gets the developer camera; null in release builds.</summary>
    public DevFlyCamera? DevCamera => _dev;

    /// <summary>Gets the viewer that streams the visible terrain around the camera; null before the body exists or without rendering.</summary>
    public VoxelViewer? VisualViewer => _visualViewer;

    /// <summary>Corrections in a row while the dev flight is on after which the client assumes the server refuses the flight.</summary>
    public const int DevFlyRefusalCorrections = 4;

    /// <summary>Gets a one-line note for the HUD (empty when there is none); set when the server refuses the dev flight.</summary>
    public string Notice { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether the dev flight of the character is switched on.</summary>
    public bool DevFlyOn => _devFlyOn;

    /// <summary>Gets the client tick counter (the number of ticks since the connection).</summary>
    public uint ClientTick => _clientTick;

    /// <summary>Gets the number of own-character snapshots the validator rejected.</summary>
    public int RejectedSnapshots => _validator?.RejectedCount ?? 0;

    /// <summary>Gets a value indicating whether prediction is paused because the server acknowledges too slowly.</summary>
    public bool PredictionPaused => _buffer?.IsPaused ?? false;

    /// <inheritdoc />
    public bool IsActive => _connected && _body is not null && _body.Mode != MovementMode.Frozen;

    /// <inheritdoc />
    public PlanetId Planet => _planet?.Id ?? default;

    /// <inheritdoc />
    public NumVector3 PredictedFeet => _body?.Feet ?? default;

    /// <inheritdoc />
    public NumQuaternion BodyRotation => _orientation?.BodyRotation ?? NumQuaternion.Identity;

    /// <inheritdoc />
    public float Pitch => _orientation?.Pitch ?? 0f;

    /// <inheritdoc />
    public NumVector3 Eye
    {
        get
        {
            var feet = PredictedFeet;
            return feet + (TerrainMath.UpAt(feet) * _rules.EyeHeight);
        }
    }

    /// <inheritdoc />
    public NumVector3 CameraOrigin => _cameraOrigin;

    /// <inheritdoc />
    public NumVector3 CameraDirection => _cameraDirection;

    /// <inheritdoc />
    public CameraMode CameraMode => _cameraMode;

    /// <summary>Gives the node its dependencies; call before it enters the tree.</summary>
    /// <param name="clientNode">The client node (its terrain provider gives the viewers their terrain).</param>
    /// <param name="runtime">The client runtime.</param>
    /// <param name="rig">The camera.</param>
    /// <param name="dev">The developer camera; null in release builds.</param>
    /// <param name="input">Where the input comes from.</param>
    /// <param name="rules">Capsule and eye of the game.</param>
    /// <param name="movement">Movement rules (the server of a host uses the same instance).</param>
    /// <param name="devFlightAllowed">Whether the character may use the dev flight.</param>
    /// <param name="render">Whether the process draws (a visual viewer follows the camera).</param>
    /// <param name="loggers">Loggers.</param>
    public void Initialize(
        ClientNode clientNode, ClientRuntime runtime, CameraRig rig, DevFlyCamera? dev, ILocalInputSource input, GameRules rules,
        MovementRules movement, bool devFlightAllowed, bool render, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(clientNode);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentNullException.ThrowIfNull(loggers);
        _clientNode = clientNode;
        _runtime = runtime;
        _rig = rig;
        _dev = dev;
        InputSource = input;
        _rules = rules;
        _movement = movement;
        _devAllowed = devFlightAllowed;
        _render = render;
        _loggers = loggers;
        _logger = loggers.CreateLogger<LocalPlayer>();
        _tickSeconds = 1f / rules.TickRate;
        _buffer = new PredictionBuffer(new CorrectionPolicy(), loggers.CreateLogger<PredictionBuffer>());
        _orientation = new OrientationTracker(movement);
        var fastest = MathF.Max(movement.MaxFallSpeed, movement.DevFlySprintSpeed) + movement.JumpSpeed;
        _monitor = new CameraContinuityMonitor(fastest, _tickSeconds);
        if (dev is not null)
        {
            dev.ToggleRequested += OnDevToggle;
            dev.OrbitRequested += OnDevOrbit;
        }
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        ProcessPhysicsPriority = PhysicsPriority;
        if (_runtime is null)
        {
            throw new InvalidOperationException("LocalPlayer.Initialize was not called before the node entered the tree.");
        }

        _runtime.AddListener(this);
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _runtime?.RemoveListener(this);
        ReleaseWorld();
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_devAllowed && @event.IsActionPressed("dev_fly_toggle"))
        {
            SetDevFly(!_devFlyOn);
        }
    }

    /// <summary>Switches the dev flight of the character (the <c>F</c> key); ignored when the build does not allow it.</summary>
    /// <param name="on">Whether the flight is on.</param>
    public void SetDevFly(bool on)
    {
        _devFlyOn = on && _devAllowed;
        _devFlyCorrections = 0;
        Notice = string.Empty;
    }

    /// <summary>Forgets the correction counters (tests measure from a settled state).</summary>
    public void ResetStatistics()
    {
        Corrections = 0;
        Compared = 0;
        MaxAckPositionError = 0f;
        _smallCorrections = _largeCorrections = 0;
        _maxPresentationOffset = 0;
    }

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello)
    {
        _hello = hello;
        _validator = OwnStateValidator.FromHello(hello, _loggers!.CreateLogger<OwnStateValidator>());
        _sequencer.Reset();
        _buffer!.Clear();
        _pending = null;
        _clientTick = 0;
        _serverMode = MovementMode.Frozen;
        _devFlyOn = false;
        ReleaseWorld();
        _connected = true;
    }

    /// <inheritdoc />
    public void OnFullSnapshot(Snapshot full)
    {
        ArgumentNullException.ThrowIfNull(full);
        // Only the newest matters: a correction starts from the latest reconstructed state.
        _pending = full;
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code)
    {
        _connected = false;
        _pending = null;
        _buffer?.Clear();
        _sequencer.Reset();
        ReleaseWorld();
    }

    /// <inheritdoc />
    public override void _PhysicsProcess(double delta)
    {
        if (!_connected || _runtime!.Session.State != ClientState.InGame)
        {
            return;
        }

        ProcessSnapshot();
        if (_body is null)
        {
            return;
        }

        _clientTick++;
        var sample = InputSource.Take();
        var devCameraActive = _dev is { IsActive: true };
        if (devCameraActive)
        {
            _dev!.Advance(sample, _tickSeconds);
        }

        var driving = devCameraActive ? LocalInputSample.None : sample;
        StepCharacter(driving);
        UpdateCamera(driving, devCameraActive);
    }

    /// <summary>
    /// The motor input of a wire input, with the fallback of the server for an invalid one: neutral, facing the last valid yaw and pitch.
    /// The client must predict exactly what <c>ServerCharacters</c> does with the same input.
    /// </summary>
    /// <param name="wire">The input as it is sent.</param>
    /// <param name="movement">Movement rules.</param>
    /// <param name="lastYaw">Yaw of the last valid input; updated on success.</param>
    /// <param name="lastPitch">Pitch of the last valid input; updated on success.</param>
    /// <returns>The input the motor gets.</returns>
    public static MotorInput ValidMotor(in PlayerInput wire, MovementRules movement, ref float lastYaw, ref float lastPitch)
    {
        ArgumentNullException.ThrowIfNull(movement);
        if (MotorInput.TryFrom(wire, movement, out var valid))
        {
            lastYaw = valid.Yaw;
            lastPitch = valid.Pitch;
            return valid;
        }

        return MotorInput.Neutral(lastYaw, lastPitch);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!_connected || _body is null)
        {
            return;
        }

        _presentation.Advance((float)delta);
        ApplyVisualPresentation(false);
    }

    private static bool OnFloorOf(MovementMode mode) => mode is MovementMode.Walking or MovementMode.Sprinting;

    private void StepCharacter(in LocalInputSample sample)
    {
        if (!WorldReadiness.IsReady)
        {
            return;
        }

        var body = _body!;
        var orientation = _orientation!;
        var feet = body.Feet;
        var up = TerrainMath.UpAt(feet);
        orientation.Advance(up, sample.MouseYaw, sample.MousePitch);
        var buttons = sample.Buttons | (_devFlyOn && _devAllowed ? InputButtons.DevFly : InputButtons.None);
        var wire = new PlayerInput(_clientTick, sample.MoveX, sample.MoveY, orientation.YawFor(up), orientation.Pitch, buttons);
        var motor = ValidMotor(wire, _movement, ref _lastYaw, ref _lastPitch);

        var seq = _sequencer.Record(_clientTick, motor);
        _validator!.NoteSent(seq);

        var paused = _buffer!.IsPaused;
        if (!paused && body.Mode != MovementMode.Frozen && GateOpen(body))
        {
            body.Step(motor);
        }

        if (!paused)
        {
            _buffer.Record(seq, motor, Capture(body, orientation.Pitch));
        }

        while (_sequencer.TryTakeBatch(out var firstSeq, out var inputs))
        {
            // The list is a view on a reused buffer: it is encoded inside this call and not kept.
            _runtime!.Session.SendInputs(firstSeq, inputs);
        }
    }

    private bool GateOpen(PlayerBody body)
    {
        if (body.OnFloor || body.Mode == MovementMode.DevFlying)
        {
            return true;
        }

        return _probe!.TryFindGround(body.GetWorld3D(), body.Feet, GateDepthMeters, out _);
    }

    private static PredictedState Capture(PlayerBody body, float pitch) =>
        new(body.Feet, body.PlanetVelocity, body.BodyRotation, pitch, body.Mode, body.OnFloor);

    private void ProcessSnapshot()
    {
        if (_pending is not { } snapshot)
        {
            return;
        }

        _pending = null;
        if (_validator is null || _validator.Validate(snapshot, out var own) != OwnStateVerdict.Accepted)
        {
            return;
        }

        var parentChanged = _hasParent && (_planet?.Id != own.Planet || _parentSeq != own.ParentSeq);
        if (parentChanged)
        {
            _buffer!.Clear();
            _presentation.Reset();
            if (_planet?.Id != own.Planet)
            {
                ReleaseWorld();
            }
        }

        _parentSeq = own.ParentSeq;
        _hasParent = true;
        _serverMode = own.Mode;
        if (_body is null && !CreateBody(own))
        {
            return;
        }

        var body = _body!;
        var open = WorldReadiness.IsReady && (own.Mode == MovementMode.DevFlying || body.OnFloor || _probe!.TryFindGround(body.GetWorld3D(), own.Feet, GateDepthMeters, out _));
        if (own.LastProcessedInputSeq == 0 || own.Mode == MovementMode.Frozen || !open || parentChanged)
        {
            // The server has not moved the body yet (or the terrain under it is not here yet): follow it, do not predict.
            _buffer!.Reconcile(own, _orientation!.Pitch, false);
            body.Place(own.Feet, own.Velocity, own.Rotation, own.Mode, OnFloorOf(own.Mode));
            _presentation.Reset();
            _snapCamera = true;
            return;
        }

        var historyCount = _buffer!.CopyUnacknowledged(_replay);
        var hasHistory = false;
        for (var i = 0; i < historyCount; i++)
        {
            hasHistory |= _replay[i].Seq == own.LastProcessedInputSeq;
        }

        var outcome = _buffer!.Reconcile(own, _orientation!.Pitch, OnFloorOf(own.Mode));
        switch (outcome)
        {
            case AckOutcome.InSync:
                _devFlyCorrections = 0;
                Compared++;
                MaxAckPositionError = MathF.Max(MaxAckPositionError, _buffer.LastPositionError);
                break;
            case AckOutcome.Correct:
                Corrections++;
                NoteCorrectionWhileFlying();
                _lastCorrection = own;
                _lastCorrectionPositionError = _buffer.LastPositionError;
                _lastCorrectionVelocityError = _buffer.LastVelocityError;
                Replay(own, !hasHistory);
                break;
            default:
                break;
        }
    }

    private void NoteCorrectionWhileFlying()
    {
        if (!_devFlyOn || ++_devFlyCorrections < DevFlyRefusalCorrections)
        {
            return;
        }

        // The server does not honour the flight bit (it runs without --dev): keep predicting what it does instead of fighting it.
        _devFlyOn = false;
        _devFlyCorrections = 0;
        Notice = "serwer nie pozwala na lot";
    }

    private void Replay(OwnState own, bool discontinuity)
    {
        var body = _body!;
        var before = Capture(body, _orientation!.Pitch);
        var count = _buffer!.CopyUnacknowledged(_replay);
        body.Place(own.Feet, own.Velocity, own.Rotation, own.Mode, OnFloorOf(own.Mode), false);
        if (_buffer.IsPaused)
        {
            // With no input progress, follow the latest server tick, including its neutral steps.
            // Keep the history for recovery, but do not advance a paused body by replaying it.
            _presentation.Reset();
            _snapCamera = true;
            _largeCorrections++;
            return;
        }

        for (var i = 0; i < count; i++)
        {
            if (body.Mode != MovementMode.Frozen && GateOpen(body))
            {
                body.Step(_replay[i].Input);
            }

            _replayStates[i] = Capture(body, _replay[i].State.Pitch);
        }

        _buffer.ReplaceUnacknowledgedStates(_replayStates.AsSpan(0, count));
        _presentation.Reconcile(before, Capture(body, _orientation.Pitch), discontinuity);
        _snapCamera |= _presentation.Snapped;
        if (_presentation.Snapped)
        {
            body.ResetPhysicsInterpolation();
            _largeCorrections++;
        }
        else
        {
            _smallCorrections++;
            _maxPresentationOffset = MathF.Max(_maxPresentationOffset, _presentation.PositionOffset.Length());
        }
    }

    private bool CreateBody(OwnState own)
    {
        var hello = _hello!;
        _planet = hello.Planets.FirstOrDefault(p => p.Id == own.Planet);
        if (_planet is null || _clientNode?.TerrainProvider is not { } provider || !provider.TryGetPlanet(own.Planet, out var terrain))
        {
            return false;
        }

        _terrain = terrain;
        _dev?.SetPlanetRadius(_planet.RadiusMeters);
        var body = PlayerBody.Instantiate();
        body.Configure(_planet, _rules, _movement, _devAllowed, CollisionLayers.ClientBodies, _tickSeconds);
        body.Name = "OwnBody";
        body.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        AddChild(body);
        body.Place(own.Feet, own.Velocity, own.Rotation, own.Mode, OnFloorOf(own.Mode));
        _body = body;
        _probe = new GroundProbe(own.Planet);
        _orientation!.Reset(own.Rotation, 0f);
        _smoother.Snap(TerrainMath.UpAt(own.Feet));
        _monitor!.Snap();
        _snapCamera = true;

        if (_render)
        {
            _visualDistance = ViewDistanceFor(0f);
            _visualViewer = new VoxelViewer { ViewDistance = _visualDistance, RequiresVisuals = true, RequiresCollisions = false };
            terrain.AddViewer(_visualViewer);
        }

        if (!_clientNode.Runtime.IsHostLocal)
        {
            // A remote client builds its own collisions around the body; a host shares the collisions of the server (spec: the host does not duplicate them).
            _collisionViewer = new VoxelViewer { ViewDistance = (uint)ServerCharacters.ServerViewerDistance, RequiresVisuals = false, RequiresCollisions = true };
            terrain.AddViewer(_collisionViewer);
        }

        _logger!.OwnBodyCreated(own.Feet.X, own.Feet.Y, own.Feet.Z, own.Mode);
        return true;
    }

    private static uint ViewDistanceFor(float altitude) => altitude switch
    {
        < 300f => 2048u,
        < 1500f => 4096u,
        < 3000f => 8192u,
        _ => 16384u,
    };

    private void UpdateCamera(in LocalInputSample sample, bool devCameraActive)
    {
        var body = _body!;
        var orientation = _orientation!;
        var rig = _rig!;
        var planet = _planet!;
        var feet = body.Feet;
        var up = TerrainMath.UpAt(feet);
        _smoother.Advance(up, _tickSeconds);

        NumVector3 origin;
        NumVector3 direction;
        if (devCameraActive)
        {
            _cameraMode = CameraMode.DevFree;
            origin = _dev!.Position;
            direction = _dev.Direction;
        }
        else
        {
            _cameraMode = rig.BodyMode;
            var pose = CameraRigMath.Compute(feet, orientation.BodyRotation, orientation.Pitch, _smoother.Up, _cameraMode, TppOffsets.Default, _rules);
            origin = _cameraMode == CameraMode.ThirdPerson ? rig.ResolveArm(planet.Id, Eye, pose.Origin) : pose.Origin;
            direction = pose.Direction;
        }

        _cameraOrigin = origin;
        _cameraDirection = direction;
        ApplyVisualPresentation(_snapCamera);
        body.SetVisualVisible(_cameraMode == CameraMode.ThirdPerson);
        if (_snapCamera)
        {
            _monitor!.Snap();
            _snapCamera = false;
        }
        else if (!devCameraActive)
        {
            _monitor!.Observe(origin, direction, MathF.Abs(sample.MouseYaw) + MathF.Abs(sample.MousePitch));
        }

    }

    private void ApplyVisualPresentation(bool teleport)
    {
        var body = _body!;
        var rig = _rig!;
        var planet = _planet!;
        body.ApplyPresentation(_presentation.PositionOffset, _presentation.RotationOffset);
        rig.Camera.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        if (_dev is { IsActive: true })
        {
            rig.Apply(planet.Id, _dev.Position, _dev.Direction, _dev.Up, teleport);
            return;
        }

        var rotation = NumQuaternion.Normalize(_presentation.RotationOffset * _orientation!.BodyRotation);
        var pose = CameraRigMath.Compute(body.Feet + _presentation.PositionOffset, rotation, _orientation.Pitch,
            _smoother.Up, _cameraMode, TppOffsets.Default, _rules);
        var origin = rig.ResolvePresentation(planet.Id, Eye, pose.Origin);
        rig.Apply(planet.Id, origin, pose.Direction, _smoother.Up, teleport);
        FollowViewers(planet, origin, body.Feet);
    }

    private void FollowViewers(PlanetParams planet, NumVector3 cameraOrigin, NumVector3 feet)
    {
        if (_visualViewer is { } visual)
        {
            var altitude = cameraOrigin.Length() - planet.RadiusMeters;
            var distance = ViewDistanceFor(altitude);
            if (distance != _visualDistance)
            {
                _visualDistance = distance;
                visual.ViewDistance = distance;
            }

            visual.GlobalPosition = PlanetFrames.ToGodot(planet.Id, cameraOrigin);
        }

        if (_collisionViewer is { } collision)
        {
            collision.GlobalPosition = PlanetFrames.ToGodot(planet.Id, feet);
        }
    }

    private void OnDevToggle()
    {
        if (_dev is null || _body is null)
        {
            return;
        }

        if (_dev.IsActive)
        {
            _dev.Exit();
            _smoother.Snap(TerrainMath.UpAt(_body.Feet));
        }
        else
        {
            _dev.Enter(_cameraOrigin, _cameraDirection);
        }

        _snapCamera = true;
    }

    private void OnDevOrbit()
    {
        if (_dev is null || _body is null)
        {
            return;
        }

        _dev.GoToOrbit(TerrainMath.UpAt(_body.Feet));
        _snapCamera = true;
    }

    private void ReleaseWorld()
    {
        _presentation.Reset();
        _hasParent = false;
        foreach (var viewer in new[] { _visualViewer, _collisionViewer })
        {
            if (viewer is not null && IsInstanceValid(viewer))
            {
                _terrain?.RemoveViewer(viewer);
                viewer.QueueFree();
            }
        }

        _visualViewer = null;
        _collisionViewer = null;
        if (_body is not null && IsInstanceValid(_body))
        {
            RemoveChild(_body);
            _body.QueueFree();
        }

        _body = null;
        _probe = null;
        _terrain = null;
    }

    private sealed class NoInput : ILocalInputSource
    {
        public LocalInputSample Take() => LocalInputSample.None;
    }
}

internal static partial class LocalPlayerLog
{
    [LoggerMessage(EventId = 9401, Level = LogLevel.Information, Message = "Own body created at {X:F1} {Y:F1} {Z:F1}, server mode {Mode}")]
    internal static partial void OwnBodyCreated(this ILogger logger, float x, float y, float z, MovementMode mode);
}
