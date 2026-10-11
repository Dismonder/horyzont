using System.Numerics;

using Horyzont.Bots.Scripting;
using Horyzont.Client.Movement;
using Horyzont.Client.Replication;
using Horyzont.Client.Session;
using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Motion;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Bots.Client;

/// <summary>
/// A client without an engine that walks the path of a player: movement goes through the <see cref="InputSequencer"/> (batches of
/// two with repeats), tools through the <see cref="ToolController"/> with the pacing of <see cref="IntentPacer"/>, and every ray
/// starts at the eye computed from the feet of the latest own state of the server (<see cref="AimRayBuilder.EyeFromFeet"/>). Only the
/// <see cref="BotRole.Abuser"/> deliberately sends what the server must refuse. One call of <see cref="Tick"/> per physics tick.
/// </summary>
public sealed class BotClient : IClientSessionListener, IFullSnapshotSink
{
    private static readonly MovementRules _movement = new();

    private readonly BotClientOptions _options;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;
    private readonly BotScript _script;
    private readonly IBotReplica _replica;
    private readonly InputSequencer _sequencer = new();
    private readonly BotToolWorldView _world = new();
    private readonly RecordingSender _sender;
    private readonly Dictionary<uint, (BotAction Kind, long SentAt)> _sent = [];
    private OwnStateValidator? _validator;
    private ToolController? _tools;
    private BlockPalette _blocks = new([]);
    private MaterialPalette _materials = new([]);
    private OwnState _own;
    private bool _hasOwn;
    private uint _clientTick;
    private float _lastYaw;
    private float _lastPitch;
    private Vector3 _home;
    private bool _hasHome;
    private bool _started;
    private bool _everDisconnected;
    private long _reconnectAt;

    /// <summary>Initializes a new instance of the <see cref="BotClient"/> class.</summary>
    /// <param name="options">Settings.</param>
    /// <param name="transport">The client transport; the caller owns and polls nothing else on it.</param>
    /// <param name="time">The clock of the session and of the intent timestamps.</param>
    /// <param name="replica">What the bot replicates; <c>null</c> for nothing.</param>
    /// <param name="loggers">Loggers; <c>null</c> for none.</param>
    public BotClient(BotClientOptions options, ITransport transport, TimeProvider time, IBotReplica? replica = null, ILoggerFactory? loggers = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(time);
        _options = options;
        _time = time;
        _loggers = loggers ?? NullLoggerFactory.Instance;
        _replica = replica ?? new NoReplica();
        _script = new BotScript(options.Seed, options.Role, options.Schedule);
        Runtime = new ClientRuntime(transport, new ClientOptions { BuildHash = options.BuildHash }, isHostLocal: false, time, _loggers);
        _sender = new RecordingSender(this, new SessionIntentSender(Runtime.Session));
        Runtime.AddListener(this);
        _replica.Attach(Runtime.Session, this);
    }

    /// <summary>Gets the client runtime (session, hello).</summary>
    public ClientRuntime Runtime { get; }

    /// <summary>Gets the counters and the intent log.</summary>
    public BotStats Stats { get; } = new();

    /// <summary>Gets the settings.</summary>
    public BotClientOptions Options => _options;

    /// <summary>Gets the replica.</summary>
    public IBotReplica Replica => _replica;

    /// <summary>Gets a value indicating whether the bot is in the game and has an own state from the server.</summary>
    public bool IsInGame => Runtime.Session.State == ClientState.InGame && _hasOwn;

    /// <summary>Gets the latest accepted own state of the server; default before the first.</summary>
    public OwnState Own => _own;

    /// <summary>Gets the player id from the hello; default before it arrived.</summary>
    public PlayerId Player => Runtime.Hello?.YourPlayer ?? default;

    /// <summary>
    /// Builds the aim of a bot standing at <paramref name="feet"/> and looking along yaw and pitch: the origin is the eye computed from
    /// the feet with the server formula, the direction is unit length (pitch up is positive).
    /// </summary>
    /// <param name="planet">Planet of the frame.</param>
    /// <param name="feet">Feet from the server.</param>
    /// <param name="yaw">Yaw in the convention of <see cref="SurfaceFrame"/>.</param>
    /// <param name="pitch">Pitch in radians.</param>
    /// <param name="rules">Game rules.</param>
    /// <returns>The aim.</returns>
    public static AimRay BuildAim(PlanetId planet, Vector3 feet, float yaw, float pitch, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var up = TerrainMath.UpAt(feet);
        var forward = Vector3.Transform(-Vector3.UnitZ, SurfaceFrame.FromYaw(up, yaw));
        var direction = Vector3.Normalize((forward * MathF.Cos(pitch)) + (up * MathF.Sin(pitch)));
        return new AimRay(planet, AimRayBuilder.EyeFromFeet(feet, rules), direction, forward, feet);
    }

    /// <summary>One physics tick: polls the connection, reconnects after a pause, and in game sends the input and the tool action of the script.</summary>
    /// <param name="scriptTick">Ticks since the epoch shared by all bots; before the epoch (negative) the bot only connects and waits.</param>
    public void Tick(long scriptTick)
    {
        Runtime.Poll();
        var state = Runtime.Session.State;
        if (state == ClientState.Disconnected)
        {
            TryConnect();
            return;
        }

        _replica.OnFrame(_hasOwn ? _own.Planet : default, _own.Feet);
        if (state != ClientState.InGame || !_hasOwn || _tools is null || scriptTick < 0)
        {
            return;
        }

        var command = _script.At(scriptTick, SenseHome());
        _clientTick++;
        var wire = new PlayerInput(
            _clientTick, command.MoveX, command.MoveY, command.Yaw, command.Pitch,
            (command.Jump ? InputButtons.Jump : InputButtons.None) | (command.Sprint ? InputButtons.Sprint : InputButtons.None));
        if (!MotorInput.TryFrom(wire, _movement, out var motor))
        {
            motor = MotorInput.Neutral(_lastYaw, _lastPitch);
        }

        _lastYaw = motor.Yaw;
        _lastPitch = motor.Pitch;
        var seq = _sequencer.Record(_clientTick, motor);
        _validator!.NoteSent(seq);
        Stats.Inputs++;
        while (_sequencer.TryTakeBatch(out var firstSeq, out var inputs))
        {
            // The list is a view on a reused buffer: it is encoded inside this call and not kept.
            Runtime.Session.SendInputs(firstSeq, inputs);
        }

        if (command.Action != BotAction.None)
        {
            Act(command.Action, motor, scriptTick);
        }
        else
        {
            _tools.ClearAim(_clientTick);
        }
    }

    /// <summary>Disconnects from the server (a normal leave).</summary>
    public void Leave() => Runtime.Session.Disconnect();

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello)
    {
        _validator = OwnStateValidator.FromHello(hello, _loggers.CreateLogger<OwnStateValidator>());
        _blocks = BlockPalette.FromHello(hello);
        _materials = MaterialPalette.FromHello(hello);
        _world.SetPlanets(hello.Planets);
        var pending = new PendingIntentTracker(NoEditPrediction.Instance, _loggers.CreateLogger<PendingIntentTracker>());
        _tools = new ToolController(_options.Rules, _world, _sender, pending, _blocks, _materials, _loggers.CreateLogger<ToolController>());
        _world.UseConstructs(_replica.Replication is { Constructs: { } mirror } replication
            ? new RemoteToolWorldView(hello, _world, mirror, replication.Entities)
            : null);
        _sequencer.Reset();
        _clientTick = 0;
        _hasOwn = false;

        // As the game client: the replica reports its terrain cache and first view, then the bot says ready at once.
        _replica.Start(hello);
        Runtime.Session.SendReady();
        Stats.Joins++;
    }

    /// <inheritdoc />
    public void OnIntentReply(IntentReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        _tools?.Pending.OnReply(reply, out _);
        if (!_sent.Remove(reply.ClientSeq, out var sent))
        {
            Stats.UnknownReplies++;
            return;
        }

        Stats.Add(new IntentRecord(reply.ClientSeq, sent.Kind, sent.SentAt, _time.GetTimestamp(), reply.Reason, reply.AppliedOpId));
    }

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _replica.OnMessage(message);
    }

    /// <inheritdoc />
    public void OnFullSnapshot(Snapshot full)
    {
        ArgumentNullException.ThrowIfNull(full);
        if (_validator is not null && _validator.Validate(full, out var own) == OwnStateVerdict.Accepted)
        {
            _own = own;
            if (!_hasHome)
            {
                _home = own.Feet;
                _hasHome = true;
            }

            _hasOwn = true;
            _world.SetFeet(own.Feet);
            Stats.Snapshots++;
        }
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code)
    {
        Stats.Disconnects++;
        _everDisconnected = true;
        _hasOwn = false;
        _sent.Clear();
        _tools?.Pending.Clear();
        _replica.OnDisconnected(code);
        _reconnectAt = _time.GetTimestamp() + (long)(_options.ReconnectDelay.TotalSeconds * _time.TimestampFrequency);
    }

    /// <inheritdoc />
    public void OnRejected(HandshakeRejectCode code, string detail)
    {
        _reconnectAt = _time.GetTimestamp() + (long)(_options.ReconnectDelay.TotalSeconds * _time.TimestampFrequency);
        _everDisconnected = true;
    }

    private BotSense SenseHome()
    {
        var feet = _own.Feet;
        var up = TerrainMath.UpAt(feet);
        var toHome = _home - feet;
        var tangent = toHome - (up * Vector3.Dot(toHome, up));
        var reference = SurfaceFrame.Reference(up);
        var side = Vector3.Cross(up, reference);
        var yaw = MathF.Atan2(Vector3.Dot(tangent, side), Vector3.Dot(tangent, reference));
        return new BotSense(yaw, tangent.Length());
    }

    private void TryConnect()
    {
        if (_started && _time.GetTimestamp() < _reconnectAt)
        {
            return;
        }

        if (_started && _everDisconnected)
        {
            Stats.Reconnects++;
        }

        _started = true;
        _everDisconnected = false;
        Runtime.Connect(_options.Server, _options.Identity, _options.Name);
    }

    private void Act(BotAction action, in MotorInput motor, long scriptTick)
    {
        var aim = BuildAim(_own.Planet, _own.Feet, motor.Yaw, motor.Pitch, _options.Rules);
        var tools = _tools!;
        var frame = default(ToolInputFrame);
        switch (action)
        {
            case BotAction.Dig:
                frame = frame with { SelectTool = ToolKind.TerrainManipulator, PrimaryPressed = true };
                break;
            case BotAction.Fill:
                frame = frame with { SelectTool = ToolKind.TerrainManipulator, SecondaryPressed = true };
                break;
            case BotAction.PlaceBlock:
                frame = frame with { SelectTool = ToolKind.Builder, SecondaryPressed = true };
                break;
            case BotAction.RemoveBlock:
                frame = frame with { SelectTool = ToolKind.Builder, PrimaryPressed = true };
                break;
            case BotAction.AbuseNotFinite:
                aim = aim with { Direction = new Vector3(float.NaN, 0f, 0f) };
                frame = frame with { SelectTool = ToolKind.TerrainManipulator, PrimaryPressed = true };
                Stats.RefusedLocally++;
                break;
            default:
                SendAbuse(action, aim);
                return;
        }

        tools.Update(frame, aim, _clientTick);
    }

    private void SendAbuse(BotAction action, in AimRay aim)
    {
        // The abuser bypasses the pacing of the tools on purpose: the server has to refuse these intents.
        _sender.Kind = action;
        switch (action)
        {
            case BotAction.AbuseOutOfReach:
                {
                    var level = BuildAim(aim.Planet, aim.Feet, 0f, 0f, _options.Rules);
                    _sender.Submit(new TerrainToolIntent(aim.Planet, level.Eye, level.Direction, 1f, TerrainEditMode.Remove, 0));
                    break;
                }

            case BotAction.AbuseBadRadius:
                _sender.Submit(new TerrainToolIntent(aim.Planet, aim.Eye, aim.Direction, 100f, TerrainEditMode.Remove, 0));
                break;
            default:
                _sender.Submit(new TerrainToolIntent(aim.Planet, aim.Eye, aim.Direction, 1f, TerrainEditMode.Remove, 0));
                break;
        }
    }

    private sealed class RecordingSender(BotClient owner, IIntentSender inner) : IIntentSender
    {
        public BotAction Kind { get; set; } = BotAction.None;

        public bool CanSend => inner.CanSend;

        public uint Submit(Intent intent)
        {
            var kind = Kind != BotAction.None ? Kind : KindOf(intent);
            var seq = inner.Submit(intent);
            owner._sent[seq] = (kind, owner._time.GetTimestamp());
            owner.Stats.IntentsSent++;
            Kind = BotAction.None;
            return seq;
        }

        private static BotAction KindOf(Intent intent) => intent switch
        {
            TerrainToolIntent { Mode: TerrainEditMode.Remove } => BotAction.Dig,
            TerrainToolIntent => BotAction.Fill,
            PlaceBlockIntent => BotAction.PlaceBlock,
            RemoveBlockIntent => BotAction.RemoveBlock,
            _ => BotAction.None,
        };
    }
}
