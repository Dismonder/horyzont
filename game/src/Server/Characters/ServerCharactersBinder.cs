using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol;
using Horyzont.Core.Simulation;
using Horyzont.Game.Frames;
using Horyzont.Server;
using Horyzont.Server.Frames;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Server.Characters;

/// <summary>
/// Connects <see cref="ServerCharacters"/> to a <see cref="ServerRuntime"/> that has not started yet: the simulation, the session and
/// the terrain only exist after the world loads, so the node is created on the first tick or the first ready player, and every
/// call is forwarded to it in the canonical places (tick between <c>Poll</c> and <c>AdvanceTick</c>, ready, leaving before
/// <c>PlayerLeaveOp</c>, shutdown step 3a). Register it with <see cref="Register"/> before <c>StartAsync</c>.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The character node belongs to the scene tree, which frees it with the server node.")]
public sealed class ServerCharactersBinder : IServerTickParticipant, IServerPlayerObserver
{
    private readonly Node _parent;
    private readonly VoxelServerTerrainFactory _terrainFactory;
    private readonly GameRules _rules;
    private readonly MovementRules _movement;
    private readonly bool _devFlightAllowed;
    private readonly ILoggerFactory _loggers;
    private readonly Func<PlanetId, System.Numerics.Vector3, bool>? _constructsReady;
    private ServerRuntime? _runtime;
    private readonly ServerFramesBinder? _frames;
    private readonly FrameNodes? _frameNodes;
    private ServerCharacters? _characters;
    private OwnStateSender? _sender;

    /// <summary>Initializes a new instance of the <see cref="ServerCharactersBinder"/> class.</summary>
    /// <param name="parent">The node the character node is added to (<c>/Root/Server</c>).</param>
    /// <param name="terrainFactory">The factory whose terrain the characters stand on (read when the world has started).</param>
    /// <param name="rules">Rules of the game.</param>
    /// <param name="movement">Movement rules.</param>
    /// <param name="devFlightAllowed">Whether the dev flight button is honoured.</param>
    /// <param name="loggers">Loggers.</param>
    /// <param name="constructsReady">Readiness of asynchronous construct collisions around a saved position.</param>
    /// <param name="frames">The server frames (K5-20): the graph the attached bodies are posed with.</param>
    /// <param name="frameNodes">The frame nodes of the server (planet nodes).</param>
    public ServerCharactersBinder(
        Node parent, VoxelServerTerrainFactory terrainFactory, GameRules rules, MovementRules movement, bool devFlightAllowed, ILoggerFactory loggers,
        Func<PlanetId, System.Numerics.Vector3, bool>? constructsReady = null, ServerFramesBinder? frames = null, FrameNodes? frameNodes = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(terrainFactory);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentNullException.ThrowIfNull(loggers);
        _parent = parent;
        _terrainFactory = terrainFactory;
        _rules = rules;
        _movement = movement;
        _devFlightAllowed = devFlightAllowed;
        _loggers = loggers;
        _constructsReady = constructsReady;
        _frames = frames;
        _frameNodes = frameNodes;
    }

    /// <summary>Gets or sets who is exhausted (K6-33 D20); handed to the characters when the world starts.</summary>
    public Func<PlayerId, bool>? ExhaustedCheck { get; set; }

    /// <summary>Gets the characters once the world has started; null before.</summary>
    public ServerCharacters? Characters => _characters;

    /// <summary>
    /// Gets the retired K2 own-state sender once the world has started; null before. It is no longer called (K3-15 replaced it with
    /// <c>EntityReplication</c>, so there is exactly one snapshot per recipient and tick); the type stays for its unit tests.
    /// </summary>
    public OwnStateSender? Sender => _sender;

    /// <summary>Registers the binder as the tick participant and the player observer of a server runtime.</summary>
    /// <param name="runtime">The runtime, not started yet.</param>
    public void Register(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        runtime.AddTickParticipant(this);
        runtime.AddPlayerObserver(this);
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var characters = Ensure(runtime);
        // K3-15: the movement step runs first; EntityReplication (registered after this binder) sends the snapshots.
        characters.Step(runtime.State.CurrentTick);
    }

    /// <inheritdoc />
    public void OnShutdown(ServerRuntime runtime) => _characters?.DespawnAll();

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player) => Ensure(_runtime ?? throw new InvalidOperationException("The binder is not registered.")).Spawn(player);

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason)
    {
        _characters?.Despawn(player);
        _sender?.Forget(player);
    }

    private ServerCharacters Ensure(ServerRuntime runtime)
    {
        if (_characters is not null)
        {
            return _characters;
        }

        var terrain = _terrainFactory.Terrain ?? throw new InvalidOperationException("The server terrain does not exist yet.");
        _characters = new ServerCharacters(
            runtime.Simulation, new SessionInputSource(runtime.Session), terrain.Voxel, _rules, _movement, _devFlightAllowed,
            _loggers.CreateLogger<ServerCharacters>(), constructsReady: _constructsReady,
            frames: _frames is { } frames ? () => frames.EnsureWorld(runtime) : null, frameNodes: _frameNodes)
        {
            ExhaustedCheck = ExhaustedCheck,
        };
        _parent.AddChild(_characters);
        _sender = new OwnStateSender(runtime.Session, runtime.Simulation, _characters);
        return _characters;
    }
}
