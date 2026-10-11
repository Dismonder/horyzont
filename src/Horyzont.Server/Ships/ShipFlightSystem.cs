using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Simulation;
using Horyzont.Core.Space;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Ships;

/// <summary>What the flight knows about a ship in the current tick (pilots read it).</summary>
/// <param name="Parent">Parent frame of the ship.</param>
/// <param name="Motion">Motion at the start of the step, in the parent frame.</param>
/// <param name="Stats">Ship stats.</param>
/// <param name="Environment">Surroundings of the step.</param>
public readonly record struct ShipFlightView(FrameRef Parent, ShipMotion Motion, ShipStats Stats, FlightEnvironment Environment);

/// <summary>
/// The flight of ships on the server (K5-24 decision 10): every tick, for each construct in <see cref="ConstructMode.Ship"/> by id,
/// the motion is kept here in <c>double</c> (rebuilt from the construct header and its <see cref="ShipStateRecord"/> when the frame,
/// the reparent counter or the stored pose changed elsewhere — zone transitions, mode switches), the control comes from
/// <see cref="IShipControlSource"/> or <see cref="AutolandController"/>, the environment from the planet or the star system and
/// <see cref="IShipTerrainGate"/>; then <see cref="ShipFlightModel.Step"/>, <see cref="IShipCollider.Sweep"/>,
/// <see cref="ShipContact.Resolve"/>, and the results go out of the journal through
/// <see cref="ServerSimulation.UpdateConstructKinematics"/> and <see cref="ServerSimulation.UpdateShipState"/>. Ships seen for the
/// first time unparked go through <see cref="ShipLoadSettler"/>. Phase <see cref="ServerTickPhase.Frames"/>, registered before the
/// zone transitions. Main thread; allocation-free after warm-up.
/// </summary>
public sealed class ShipFlightSystem : IServerTickParticipant, IShipStatusSource
{
    private readonly ServerSimulation _simulation;
    private readonly ShipStatsCache _stats;
    private readonly IShipCollider _collider;
    private readonly IShipTerrainGate _gate;
    private readonly FlightRules _rules;
    private readonly ShipLoadSettler _settler;
    private readonly float _dt;
    private readonly ILogger _logger;
    private readonly Dictionary<EntityId, Entry> _entries = [];
    private readonly List<EntityId> _ships = [];
    private readonly List<EntityId> _gone = [];
    private StarSystemModel? _model;

    /// <summary>Initializes a new instance of the <see cref="ShipFlightSystem"/> class.</summary>
    /// <param name="simulation">The server simulation.</param>
    /// <param name="stats">Ship stats.</param>
    /// <param name="control">Pilot input.</param>
    /// <param name="collider">Hull collision.</param>
    /// <param name="gate">Terrain gate.</param>
    /// <param name="rules">Flight rules.</param>
    /// <param name="tickRate">Server ticks per second.</param>
    /// <param name="logger">Logger.</param>
    public ShipFlightSystem(
        ServerSimulation simulation, ShipStatsCache stats, IShipControlSource control, IShipCollider collider, IShipTerrainGate gate,
        FlightRules rules, int tickRate, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(collider);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tickRate, 0);
        _simulation = simulation;
        _stats = stats;
        Control = control;
        _collider = collider;
        _gate = gate;
        _rules = rules;
        _dt = 1f / tickRate;
        _logger = logger;
        _settler = new ShipLoadSettler(collider, gate, rules, logger);
    }

    /// <inheritdoc />
    public ServerTickPhase Phase => ServerTickPhase.Frames;

    /// <summary>Gets or sets the pilot input (development pilots wrap the default one).</summary>
    public IShipControlSource Control { get; set; }

    /// <summary>Gets or sets the launch guard (K5-25 decision 9); null = no guard.</summary>
    public IShipLaunchGuard? LaunchGuard { get; set; }

    /// <summary>Gets the flight rules.</summary>
    public FlightRules Rules => _rules;

    /// <summary>Gets the number of ships with a flight entry.</summary>
    public int Count => _entries.Count;

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Step();
    }

    /// <summary>One tick of flight for every ship.</summary>
    public void Step()
    {
        var state = _simulation.State;
        if (state.StarSystem is { } system && (_model is null || !ReferenceEquals(_model.System, system)))
        {
            _model = new StarSystemModel(system, state.Planets.All);
        }

        _ships.Clear();
        var all = state.Constructs.All;
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Mode == ConstructMode.Ship)
            {
                _ships.Add(all[i].Id);
            }
        }

        foreach (var id in _entries.Keys)
        {
            if (!state.Constructs.TryGet(id, out var c) || c.Mode != ConstructMode.Ship)
            {
                _gone.Add(id);
            }
        }

        for (var i = 0; i < _gone.Count; i++)
        {
            _entries.Remove(_gone[i]);
            _collider.Forget(_gone[i]);
            _gate.Forget(_gone[i]);
        }

        _gone.Clear();
        for (var i = 0; i < _ships.Count; i++)
        {
            StepShip(_ships[i], state.CurrentTick);
        }
    }

    /// <summary>Status of a ship for the HUD and <c>ShipStatus</c> (K5-26).</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="status">Receives the status.</param>
    /// <returns>False for a construct the flight has not stepped.</returns>
    public bool TryGetStatus(EntityId ship, out ShipFlightStatus status)
    {
        if (_entries.TryGetValue(ship, out var entry) && entry.Stepped)
        {
            status = entry.Status;
            return true;
        }

        status = default;
        return false;
    }

    /// <summary>
    /// The flight view of a ship in the current tick (valid while the control source is asked). After the tick: the motion after
    /// the step and the environment of that step (gate, launch block after the guard, horizontal cap), K5-25 decision 15.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <param name="view">Receives the view.</param>
    /// <returns>False for a construct the flight does not know.</returns>
    public bool TryGetView(EntityId ship, out ShipFlightView view)
    {
        if (_entries.TryGetValue(ship, out var entry) && entry.Stats is { } stats)
        {
            view = new ShipFlightView(entry.Parent, entry.Motion, stats, entry.Environment);
            return true;
        }

        view = default;
        return false;
    }

    /// <summary>Distance from a ship down to what it would hit along the up direction of its environment.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="probe">Probe length (metres).</param>
    /// <returns>The distance; infinite without a hit.</returns>
    public double ProbeGround(EntityId ship, double probe)
    {
        if (!_entries.TryGetValue(ship, out var entry))
        {
            return double.PositiveInfinity;
        }

        var from = entry.Motion.Pose;
        var down = new PoseD(from.Position - (entry.Environment.Up * probe), from.Rotation);
        var hit = _collider.Sweep(ship, from, down);
        return hit.Hit ? hit.Fraction * probe : double.PositiveInfinity;
    }

    /// <summary>The star system model of the world; null without a system.</summary>
    /// <returns>The model.</returns>
    public StarSystemModel? SystemModel() => _model;

    /// <summary>Sets the fuel of a ship (console, tests): the stored state and the running motion.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="fuel">Fuel units, clipped to the capacity.</param>
    /// <returns>False for a construct that is not a ship.</returns>
    public bool SetFuel(EntityId ship, float fuel)
    {
        if (!_stats.TryGet(ship, out var stats) || !float.IsFinite(fuel))
        {
            return false;
        }

        fuel = Math.Clamp(fuel, 0f, stats.FuelCapacity);
        var record = _simulation.State.ShipStates.TryGet(ship, out var current)
            ? current with { Fuel = fuel }
            : new ShipStateRecord(ship, fuel, default, default, ShipStateFlags.Parked | ShipStateFlags.GearDown);
        if (!_simulation.UpdateShipState(ship, record))
        {
            return false;
        }

        if (_entries.TryGetValue(ship, out var entry))
        {
            entry.Motion.Fuel = fuel;
            entry.Record = record;
        }

        return true;
    }

    private void StepShip(EntityId id, Tick tick)
    {
        var state = _simulation.State;
        if (!state.Constructs.TryGet(id, out var ship) || !_stats.TryGet(id, out var stats))
        {
            return;
        }

        if (!_entries.TryGetValue(id, out var entry))
        {
            entry = new Entry();
            _entries.Add(id, entry);
            Restore(entry, ship, keepVelocity: false);
            entry.Settling = (entry.Motion.Flags & ShipFlightFlags.Parked) == 0;
        }
        else if (ship.Parent != entry.Parent || ship.ParentSeq != entry.ParentSeq || ship.Origin != entry.Origin || ship.Rotation != entry.Rotation)
        {
            Restore(entry, ship, keepVelocity: true);
        }
        else if (state.ShipStates.TryGet(id, out var stored) && stored != entry.Record)
        {
            // A refuel (ShipFuelAddOp) or a replay changed the stored state.
            entry.Motion.Fuel = stored.Fuel;
            entry.Record = stored;
        }

        entry.Stats = stats;
        var motion = entry.Motion;
        var environment = Environment(ship, entry, ref motion, stats);
        entry.Environment = environment;
        entry.Motion = motion;

        if (entry.Settling)
        {
            var result = _settler.Settle(id, ref motion, stats, environment, ship.Parent.Kind == FrameKind.Planet ? new PlanetId(ship.Parent.Id) : null);
            entry.Settling = result == ShipSettleResult.Holding;
        }
        else
        {
            if (!Control.TryGetControl(id, tick, out var control))
            {
                control = AutolandController.Compute(motion, environment, stats, _rules);
                motion.Flags |= ShipFlightFlags.Autoland;
            }
            else
            {
                motion.Flags &= ~ShipFlightFlags.Autoland;

                // K5-25 decision 9 (R3): a pilot trying to take off from a parked ship (gear up) asks the guard; the fuel reserve wins.
                if (LaunchGuard is { } guard && (motion.Flags & ShipFlightFlags.Parked) != 0 && environment.LaunchBlock == ShipLaunchBlock.None
                    && (control.Buttons & ShipButtons.LandingGear) == 0)
                {
                    environment = environment with { LaunchBlock = guard.Evaluate(id, tick) };
                    entry.Environment = environment;
                }
            }

            var from = motion.Pose;
            var outcome = ShipFlightModel.Step(ref motion, control, stats, environment, _rules, _dt);
            entry.SpeedLimit = outcome.SpeedLimit;
            if (!PoseEquals(from, motion.Pose))
            {
                ShipContact.Resolve(ref motion, from, _collider.Sweep(id, from, motion.Pose), environment, _rules);
            }
        }

        entry.Motion = motion;
        Store(id, ship, entry, stats, environment);
    }

    private FlightEnvironment Environment(Construct ship, Entry entry, ref ShipMotion motion, ShipStats stats)
    {
        var com = motion.Pose.Position + motion.Pose.Rotation.Rotate(Vector3D.FromFloat(stats.CenterOfMass));
        FlightEnvironment environment;
        if (ship.Parent.Kind == FrameKind.Planet && _simulation.State.Planets.TryGet(new PlanetId(ship.Parent.Id), out var planet))
        {
            var index = _model is { } model && model.TryGetBody(planet.Id, out var body) ? body.BodyIndex : 0;
            environment = FlightEnvironments.InPlanet(planet, index, com, _rules, _model);
            var gate = _gate.Query(ship.Id, planet.Id, motion, stats, out var cap);
            // K5-25 R2: the cap travels as a float in ShipCorrection, so the server steps with the same rounded number.
            environment = environment with { Gate = gate, HorizontalSpeedCap = (double)(float)cap };
        }
        else if (ship.Parent.Kind == FrameKind.System && _model is { } system)
        {
            environment = FlightEnvironments.InSystem(system, com, _rules);
        }
        else
        {
            environment = new FlightEnvironment { Up = new Vector3D(0, 1, 0), HorizontalSpeedCap = double.PositiveInfinity, Gate = ShipTerrainGate.Unavailable };
        }

        var flags = motion.Flags & ~(ShipFlightFlags.TerrainHold | ShipFlightFlags.TerrainUnavailable);
        if (environment.Gate is ShipTerrainGate.NotLoaded or ShipTerrainGate.CollisionPending)
        {
            flags |= ShipFlightFlags.TerrainHold;
        }
        else if (environment.Gate == ShipTerrainGate.Unavailable)
        {
            flags |= ShipFlightFlags.TerrainUnavailable;
        }

        motion.Flags = flags;
        var parkedOnReserve = (flags & ShipFlightFlags.Parked) != 0 && ShipFlightModel.IsReserve(motion.Fuel, stats, _rules);
        return environment with { LaunchBlock = parkedOnReserve ? ShipLaunchBlock.FuelReserve : ShipLaunchBlock.None };
    }

    private void Restore(Entry entry, Construct ship, bool keepVelocity)
    {
        entry.Parent = ship.Parent;
        entry.ParentSeq = ship.ParentSeq;
        entry.Origin = ship.Origin;
        entry.Rotation = ship.Rotation;
        var motion = entry.Motion;
        motion.Pose = new PoseD(Vector3D.FromFloat(ship.Origin), QuaternionD.FromFloat(ship.Rotation));
        if (!keepVelocity)
        {
            var record = _simulation.State.ShipStates.TryGet(ship.Id, out var stored)
                ? stored
                : new ShipStateRecord(ship.Id, 0f, default, default, ShipStateFlags.Parked | ShipStateFlags.GearDown);
            entry.Record = record;
            motion.Fuel = record.Fuel;
            motion.LinearVelocity = record.LinearVelocity;
            motion.AngularVelocity = record.AngularVelocity;
            motion.Flags = FromState(record.Flags);
        }

        entry.Motion = motion;
    }

    private void Store(EntityId id, Construct ship, Entry entry, ShipStats stats, in FlightEnvironment environment)
    {
        var motion = entry.Motion;
        if (!PoseEquals(motion.Pose, new PoseD(Vector3D.FromFloat(entry.Origin), QuaternionD.FromFloat(entry.Rotation))))
        {
            if (FrameMath.TryToFloat(motion.Pose, ship.Parent.Kind, out var origin, out var rotation)
                && _simulation.UpdateConstructKinematics(id, ship.Parent, ship.ParentSeq, origin, rotation))
            {
                entry.Origin = origin;
                entry.Rotation = rotation;
            }
            else
            {
                _logger.PoseOutOfRange(id.Value, ship.Parent.ToString());
                motion.Pose = new PoseD(Vector3D.FromFloat(entry.Origin), QuaternionD.FromFloat(entry.Rotation));
                motion.LinearVelocity = default;
                motion.AngularVelocity = default;
                entry.Motion = motion;
            }
        }

        var record = new ShipStateRecord(id, MathF.Max(motion.Fuel, 0f), motion.LinearVelocity, motion.AngularVelocity, ToState(motion.Flags));
        if (record != entry.Record && _simulation.UpdateShipState(id, record))
        {
            entry.Record = record;
        }

        if (entry.SpeedLimit <= 0)
        {
            entry.SpeedLimit = _rules.SpeedLimit(environment.Altitude, (motion.Flags & ShipFlightFlags.PulseEngaged) != 0);
        }

        entry.Status = new ShipFlightStatus(motion.Fuel, stats.FuelCapacity, (float)entry.SpeedLimit, motion.Flags, environment.LaunchBlock);
        entry.Stepped = true;
    }

    private static bool PoseEquals(in PoseD a, in PoseD b) =>
        a.Position == b.Position && a.Rotation.X == b.Rotation.X && a.Rotation.Y == b.Rotation.Y && a.Rotation.Z == b.Rotation.Z
        && a.Rotation.W == b.Rotation.W;

    /// <summary>Flight flags of a stored state.</summary>
    /// <param name="flags">Stored flags.</param>
    /// <returns>Flight flags.</returns>
    public static ShipFlightFlags FromState(ShipStateFlags flags)
    {
        var result = default(ShipFlightFlags);
        if ((flags & ShipStateFlags.Parked) != 0)
        {
            result |= ShipFlightFlags.Parked;
        }

        if ((flags & ShipStateFlags.GearDown) != 0)
        {
            result |= ShipFlightFlags.GearDown;
        }

        if ((flags & ShipStateFlags.Assist) != 0)
        {
            result |= ShipFlightFlags.Assist;
        }

        return result;
    }

    /// <summary>Stored flags of flight flags.</summary>
    /// <param name="flags">Flight flags.</param>
    /// <returns>Stored flags.</returns>
    public static ShipStateFlags ToState(ShipFlightFlags flags)
    {
        var result = default(ShipStateFlags);
        if ((flags & ShipFlightFlags.Parked) != 0)
        {
            result |= ShipStateFlags.Parked;
        }

        if ((flags & ShipFlightFlags.GearDown) != 0)
        {
            result |= ShipStateFlags.GearDown;
        }

        if ((flags & ShipFlightFlags.Assist) != 0)
        {
            result |= ShipStateFlags.Assist;
        }

        return result;
    }

    [SuppressMessage("Performance", "CA1812", Justification = "Instantiated in StepShip.")]
    private sealed class Entry
    {
#pragma warning disable SA1401 // Mutable struct fields modified in place.
        public ShipMotion Motion;
#pragma warning restore SA1401

        public FrameRef Parent { get; set; }

        public uint ParentSeq { get; set; }

        public System.Numerics.Vector3 Origin { get; set; }

        public System.Numerics.Quaternion Rotation { get; set; }

        public ShipStateRecord Record { get; set; }

        public ShipStats? Stats { get; set; }

        public FlightEnvironment Environment { get; set; }

        public double SpeedLimit { get; set; }

        public ShipFlightStatus Status { get; set; }

        public bool Stepped { get; set; }

        public bool Settling { get; set; }
    }
}
