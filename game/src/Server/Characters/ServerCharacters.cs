using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Character;
using Horyzont.Game.Physics;
using Horyzont.Game.Terrain;
using Horyzont.Server;

using Microsoft.Extensions.Logging;

using NumQuaternion = System.Numerics.Quaternion;
using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Server.Characters;

/// <summary>
/// The authoritative bodies of all players (K2-11): one <see cref="PlayerBody"/> per online player, one input per tick from the
/// session, <see cref="CharacterMotor"/> and <c>MoveAndSlide</c> for the physics, and <c>UpdatePlayerKinematics</c> for the result.
/// The node does not process by itself: the server loop calls <see cref="Step"/> once per tick between <c>ServerSession.Poll</c>
/// and <c>ServerSimulation.AdvanceTick</c> (through <see cref="IServerTickParticipant"/>), because the order of
/// <c>_PhysicsProcess</c> calls must not matter on the server. Every body carries a <see cref="VoxelViewer"/> that builds the terrain
/// collision around it without meshes.
/// </summary>
public sealed partial class ServerCharacters : Node3D, IServerTickParticipant, IServerPlayerObserver, IServerCharacterSource
{
    /// <summary>Radius (metres) of the collision viewer of a body: reach of the tools (8 m) plus room for <c>IsAreaLoaded</c>.</summary>
    public const float ServerViewerDistance = 64f;

    /// <summary>How far under a frozen body the ground wait looks for collision (metres).</summary>
    public const float GroundSearchMeters = 8f;

    /// <summary>Height above the found collision where a body that waited for the ground is put (metres).</summary>
    public const float GroundClearance = 0.05f;

    private const int InvalidInputLogEvery = 64;

    private readonly ServerSimulation _simulation;
    private readonly IInputSource _inputs;
    private readonly VoxelTerrainService _terrain;
    private readonly GameRules _rules;
    private readonly MovementRules _movement;
    private readonly bool _devFlightAllowed;
    private readonly Func<PlanetParams, NumVector3, NumVector3> _rescue;
    private readonly Func<PlanetId, NumVector3, bool> _constructsReady;
    private readonly ILogger<ServerCharacters> _logger;
    private readonly List<Slot> _ordered = [];
    private readonly Dictionary<PlayerId, Slot> _byPlayer = [];
    private readonly float _tickSeconds;

    /// <summary>Creates the node; add it to the server tree before the first <see cref="Spawn"/>.</summary>
    /// <param name="simulation">The simulation whose player kinematics are written.</param>
    /// <param name="inputs">Where the inputs come from.</param>
    /// <param name="terrain">The terrain of the server (the planets the viewers stream).</param>
    /// <param name="rules">Capsule and eye of the game.</param>
    /// <param name="movement">Movement rules.</param>
    /// <param name="devFlightAllowed">Whether the dev flight button is honoured.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="rescue">Feet position above the surface for a direction from the planet centre (the rescue from below the core); the generator of the planet by default.</param>
    /// <param name="constructsReady">Whether collision at the saved pose is installed, including asynchronous construct meshing.</param>
    public ServerCharacters(
        ServerSimulation simulation, IInputSource inputs, VoxelTerrainService terrain, GameRules rules, MovementRules movement,
        bool devFlightAllowed, ILogger<ServerCharacters> logger, Func<PlanetParams, NumVector3, NumVector3>? rescue = null,
        Func<PlanetId, NumVector3, bool>? constructsReady = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentNullException.ThrowIfNull(logger);
        _simulation = simulation;
        _inputs = inputs;
        _terrain = terrain;
        _rules = rules;
        _movement = movement;
        _devFlightAllowed = devFlightAllowed;
        _rescue = rescue ?? SpawnLocator.AboveSurface;
        _constructsReady = constructsReady ?? ((_, _) => true);
        _logger = logger;
        _tickSeconds = 1f / rules.TickRate;
        Name = "Characters";
        SetPhysicsProcess(false);
    }

    /// <summary>Gets the number of bodies.</summary>
    public int Count => _ordered.Count;

    /// <summary>Gets the tick of the latest <see cref="Step"/>.</summary>
    public Tick LastTick { get; private set; }

    /// <summary>Gets the number of ticks after which a frozen body that never found collision is let go.</summary>
    public int GroundWaitDeadlineTicks => _movement.GroundWaitDeadlineTicks;

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Step(runtime.State.CurrentTick);
    }

    /// <inheritdoc />
    public void OnShutdown(ServerRuntime runtime) => DespawnAll();

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player) => Spawn(player);

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason) => Despawn(player);

    /// <summary>Creates the body of a player from the pose in the simulation; it stays frozen until the terrain collision exists.</summary>
    /// <param name="player">The online player.</param>
    public void Spawn(PlayerId player)
    {
        if (_byPlayer.ContainsKey(player))
        {
            return;
        }

        if (!_simulation.State.Players.TryGet(player, out var state) || !state.IsOnline)
        {
            _logger.SpawnRefused(player.Value, "the player is not online");
            return;
        }

        if (state.Frame.Kind != FrameKind.Planet || !_simulation.State.Planets.TryGet(new PlanetId(state.Frame.Id), out var planet))
        {
            _logger.SpawnRefused(player.Value, "the player is not on a planet");
            return;
        }

        if (!_terrain.TryGetPlanet(planet.Id, out _))
        {
            _logger.SpawnRefused(player.Value, "the planet has no terrain");
            return;
        }

        var body = PlayerBody.Instantiate();
        body.Configure(planet, _rules, _movement, _devFlightAllowed, CollisionLayers.ServerBodies, _tickSeconds);
        body.SetVisualVisible(false);
        body.Name = $"Body_{player.Value}";
        AddChild(body);
        body.Place(state.Position, NumVector3.Zero, NumQuaternion.Normalize(state.BodyRotation), MovementMode.Frozen, false);
        var viewer = new VoxelViewer { ViewDistance = (uint)ServerViewerDistance, RequiresVisuals = false, RequiresCollisions = true };
        body.AddChild(viewer);

        var character = new Slot(player, planet, body, new GroundProbe(planet.Id, SpawnSupport.CollisionMask)) { LastPitch = state.Pitch };
        var index = _ordered.FindIndex(other => other.Player.Value > player.Value);
        _ordered.Insert(index < 0 ? _ordered.Count : index, character);
        _byPlayer.Add(player, character);
        _logger.Spawned(player.Value, state.Position.X, state.Position.Y, state.Position.Z);
    }

    /// <summary>Writes the last kinematics of a player and removes the body and its viewer. Call before <c>PlayerLeaveOp</c>.</summary>
    /// <param name="player">The player; unknown players are ignored.</param>
    public void Despawn(PlayerId player)
    {
        if (!_byPlayer.Remove(player, out var character))
        {
            return;
        }

        _ordered.Remove(character);
        if (!_simulation.IsFaulted)
        {
            Publish(character);
        }

        Release(character);
        _logger.Despawned(player.Value);
    }

    /// <summary>Despawns every body: the shutdown step before <c>LeaveAllOnlinePlayers</c>.</summary>
    public void DespawnAll()
    {
        while (_ordered.Count > 0)
        {
            Despawn(_ordered[0].Player);
        }
    }

    /// <summary>One tick for every body, in ascending player id order.</summary>
    /// <param name="tick">The tick of the simulation (for logs and tests; the physics step is one tick long).</param>
    public void Step(Tick tick)
    {
        LastTick = tick;
        for (var i = 0; i < _ordered.Count; i++)
        {
            StepOne(_ordered[i]);
        }
    }

    /// <summary>Reads the state of a body.</summary>
    /// <param name="player">The player.</param>
    /// <param name="state">The state when the player has a body.</param>
    /// <returns>Whether the player has a body.</returns>
    public bool TryGetState(PlayerId player, out ServerCharacterState state)
    {
        if (!_byPlayer.TryGetValue(player, out var character) || !_simulation.State.Players.TryGet(player, out var simulated))
        {
            state = default;
            return false;
        }

        var body = character.Body;
        state = new ServerCharacterState(
            character.Planet.Id, simulated.Frame, simulated.ParentSeq, body.Feet, body.BodyRotation, character.LastPitch, body.PlanetVelocity,
            body.Mode, character.LastSeq);
        return true;
    }

    private void StepOne(Slot c)
    {
        if (!_simulation.State.Players.TryGet(c.Player, out var simulated) || !simulated.IsOnline)
        {
            return;
        }

        var body = c.Body;
        if (c.Resync)
        {
            // The simulation refused the last kinematics (a stale frame or a bad pose): go back to what it holds and find the ground again.
            c.Resync = false;
            body.Place(simulated.Position, NumVector3.Zero, NumQuaternion.Normalize(simulated.BodyRotation), MovementMode.Frozen, false);
            c.FrozenTicks = 0;
        }

        var input = ReadInput(c, out var hasInput);
        if (body.Mode == MovementMode.Frozen)
        {
            StepFrozen(c);
        }
        else if (hasInput)
        {
            body.Step(input);
            if (RescueIfBelowCore(c))
            {
                return;
            }
        }

        Publish(c);
    }

    private MotorInput ReadInput(Slot c, out bool hasInput)
    {
        if (_inputs.TryTakeInput(c.Player, out var wire, out var seq))
        {
            c.LastSeq = seq;
            c.Starved = 0;
            hasInput = true;
            if (MotorInput.TryFrom(wire, _movement, out var valid))
            {
                c.LastYaw = valid.Yaw;
                c.LastPitch = valid.Pitch;
                return valid;
            }

            if (c.InvalidInputs++ % InvalidInputLogEvery == 0)
            {
                _logger.InvalidInput(c.Player.Value, seq, c.InvalidInputs);
            }

            return MotorInput.Neutral(c.LastYaw, c.LastPitch);
        }

        c.Starved++;
        hasInput = c.Starved > _movement.MaxStarvedTicks;
        if (c.Starved == _movement.MaxStarvedTicks + 1)
        {
            _logger.InputStarved(c.Player.Value, c.Starved);
        }

        return MotorInput.Neutral(c.LastYaw, c.LastPitch);
    }

    private void StepFrozen(Slot c)
    {
        var body = c.Body;
        c.FrozenTicks++;
        var constructsReady = _constructsReady(c.Planet.Id, body.Feet);
        var terrainReady = _terrain.IsAreaLoaded(c.Planet.Id, body.Feet, GroundSearchMeters);
        if (SpawnSupport.CanSettle(terrainReady, constructsReady)
            && c.Probe.TryFindGround(body.GetWorld3D(), body.Feet, GroundSearchMeters, out var ground, GroundClearance))
        {
            var up = TerrainMath.UpAt(ground);
            body.Place(SpawnSupport.FeetOnSupport(body.Feet, ground, up, GroundClearance), NumVector3.Zero,
                SurfaceFrame.FromYaw(up, c.LastYaw), MovementMode.Walking, false);
            _logger.Grounded(c.Player.Value, c.FrozenTicks, MovementMode.Walking);
            c.FrozenTicks = 0;
            return;
        }

        if (c.FrozenTicks >= _movement.GroundWaitDeadlineTicks && SpawnSupport.CanExpire(constructsReady))
        {
            _logger.GroundWaitExpired(c.Player.Value, c.FrozenTicks);
            body.SetMode(MovementMode.Falling);
            c.FrozenTicks = 0;
        }
    }

    private bool RescueIfBelowCore(Slot c)
    {
        var body = c.Body;
        var feet = body.Feet;
        var planet = c.Planet;
        var floor = planet.RadiusMeters - planet.CoreDepthMeters - _movement.CoreRescueMarginMeters;
        var radius = feet.Length();
        if (radius >= floor)
        {
            return false;
        }

        _logger.RescuedFromCore(c.Player.Value, radius);
        var target = _rescue(planet, feet);
        body.Place(target, NumVector3.Zero, SurfaceFrame.FromYaw(TerrainMath.UpAt(target), c.LastYaw), MovementMode.Frozen, false);
        c.FrozenTicks = 0;
        Publish(c);
        return true;
    }

    private void Publish(Slot c)
    {
        if (!_simulation.State.Players.TryGet(c.Player, out var simulated))
        {
            return;
        }

        var body = c.Body;
        var rotation = NumQuaternion.Normalize(body.BodyRotation);
        if (!_simulation.UpdatePlayerKinematics(c.Player, simulated.Frame, simulated.ParentSeq, body.Feet, rotation, c.LastPitch))
        {
            if (c.Refused++ % InvalidInputLogEvery == 0)
            {
                _logger.KinematicsRefused(c.Player.Value, c.Refused);
            }

            c.Resync = true;
        }
    }

    private void Release(Slot character)
    {
        var body = character.Body;
        if (body.GetParent() == this)
        {
            RemoveChild(body);
        }

        body.QueueFree();
    }

    private sealed class Slot(PlayerId player, PlanetParams planet, PlayerBody body, GroundProbe probe)
    {
        public PlayerId Player { get; } = player;

        public PlanetParams Planet { get; } = planet;

        public PlayerBody Body { get; } = body;

        public GroundProbe Probe { get; } = probe;

        public int Starved { get; set; }

        public int FrozenTicks { get; set; }

        public int InvalidInputs { get; set; }

        public int Refused { get; set; }

        public bool Resync { get; set; }

        public uint LastSeq { get; set; }

        public float LastYaw { get; set; }

        public float LastPitch { get; set; }
    }
}
