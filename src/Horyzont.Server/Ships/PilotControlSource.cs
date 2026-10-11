using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Ships;

/// <summary>
/// The pilot at the controls (K5-25 decision 13): the online player in the control cockpit; one input slot per server tick.
/// Each tick consumes slot <c>NextSeq</c> (the input, or a substitute when it is late or lost) and advances, so the number of a
/// correction always matches the state after its step (R1). Without an online pilot the flight autolands.
/// </summary>
public sealed class PilotControlSource : IShipControlSource
{
    /// <summary>The newest buffered input may run this far ahead of the next slot before the ahead counter grows.</summary>
    public const int AheadSlack = 6;

    /// <summary>Ticks the client must stay ahead before the slots are re-anchored.</summary>
    public const int AheadTicks = 60;

    /// <summary>Missing slots in a row (with later inputs present) before the slots are re-anchored.</summary>
    public const int BehindMissing = 3;

    private const ulong LogIntervalTicks = 600;

    /// <summary>The controls a new pilot starts from until its first input: no thrust, gear down, assist on.</summary>
    private static readonly ShipControl Neutral = new(0, 0, 0, 0, 0, 0, ShipButtons.Assist | ShipButtons.LandingGear);

    private readonly ServerSimulation _simulation;
    private readonly ShipStatsCache _stats;
    private readonly IShipInputSource _inputs;
    private readonly SeatRules _rules;
    private readonly ILogger _logger;
    private readonly Dictionary<EntityId, Slots> _slots = [];
    private ulong _lastTick = ulong.MaxValue;
    private ulong _lastNotPilotLog;
    private ulong _lastReanchorLog;
    private bool _logged;
    private bool _reanchorLogged;

    /// <summary>Creates the source.</summary>
    /// <param name="simulation">The server simulation.</param>
    /// <param name="stats">Ship stats (control cockpit).</param>
    /// <param name="inputs">Buffered pilot inputs.</param>
    /// <param name="logger">Logger.</param>
    public PilotControlSource(ServerSimulation simulation, ShipStatsCache stats, IShipInputSource inputs, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(logger);
        _simulation = simulation;
        _stats = stats;
        _inputs = inputs;
        _rules = simulation.Rules.Seats;
        _logger = logger;
    }

    /// <summary>Gets the counters.</summary>
    public ShipInputStats Stats { get; } = new();

    /// <inheritdoc />
    public bool TryGetControl(EntityId ship, Tick tick, out ShipControl control)
    {
        if (tick.Value != _lastTick)
        {
            _lastTick = tick.Value;
            DropNonPilotInputs(tick.Value);
        }

        control = default;
        if (!_stats.TryGet(ship, out var stats) || !SeatQueries.TryGetPilot(_simulation.State, ship, stats, out var pilot) || !pilot.IsOnline)
        {
            if (_slots.TryGetValue(ship, out var gone))
            {
                gone.Anchored = false;
                gone.HasLast = false;
            }

            return false;
        }

        if (!_slots.TryGetValue(ship, out var slots))
        {
            slots = new Slots();
            _slots.Add(ship, slots);
        }

        if (slots.Player != pilot.Player || !slots.Anchored && slots.HasLast)
        {
            // A new pilot (or a pilot back after a gap): anchor again on its newest input; earlier controls do not carry over.
            slots.Player = pilot.Player;
            slots.Anchored = false;
            slots.HasLast = false;
            slots.Last = Neutral;
        }

        if (!slots.Anchored && !Anchor(slots))
        {
            return false;
        }

        if (CheckDrift(ship, slots, tick.Value))
        {
            // Drift is only seen with inputs received, so the re-anchoring finds a number; should it not, the old slots go on.
            _ = Anchor(slots);
        }

        if (_inputs.TryTakeShipInput(slots.Player, slots.NextSeq, out var input))
        {
            control = input.Control;
            slots.Last = control;
            slots.MissingRun = 0;
            Stats.Consumed++;
        }
        else
        {
            slots.MissingRun++;
            Stats.Substituted++;
            var last = slots.Last;
            control = slots.MissingRun > _rules.HoldLastInputTicks ? new ShipControl(0, 0, 0, 0, 0, 0, last.Buttons) : last;
        }

        slots.HasLast = true;
        slots.LastProcessedSeq = slots.NextSeq;
        slots.NextSeq++;
        return true;
    }

    /// <summary>The input slot the last step of a ship consumed (the number of its correction, K5-25 decision 15).</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="player">The pilot whose slot it was.</param>
    /// <param name="seq">The slot.</param>
    /// <returns>False when the ship has no anchored pilot.</returns>
    public bool TryGetLastProcessedSeq(EntityId ship, out PlayerId player, out uint seq)
    {
        if (_slots.TryGetValue(ship, out var slots) && slots.Anchored && slots.HasLast)
        {
            player = slots.Player;
            seq = slots.LastProcessedSeq;
            return true;
        }

        player = default;
        seq = 0;
        return false;
    }

    private bool Anchor(Slots slots)
    {
        if (!_inputs.TryPeekNewestShipSeq(slots.Player, out var newest))
        {
            return false;
        }

        var delay = (uint)Math.Max(0, _rules.InputDelayTicks);
        slots.NextSeq = newest > delay ? newest - delay : 1;
        _inputs.RewindShipInputs(slots.Player, slots.NextSeq); // behind: slots from NextSeq on may be consumed already
        slots.Anchored = true;
        slots.MissingRun = 0;
        slots.AheadRun = 0;
        slots.LateSeen = _inputs.ShipInputLateCount(slots.Player);
        return true;
    }

    /// <summary>
    /// True when the client clock drifted: the slots must be re-anchored. Behind (new inputs arrive for consumed slots) the buffer
    /// is rewound by the re-anchoring, not cleared, so the newest number is known and the pilot keeps the controls (R1).
    /// </summary>
    private bool CheckDrift(EntityId ship, Slots slots, ulong tick)
    {
        var hasNewest = _inputs.TryPeekNewestShipSeq(slots.Player, out var newest);
        slots.AheadRun = hasNewest && newest > slots.NextSeq + AheadSlack ? slots.AheadRun + 1 : 0;
        var late = _inputs.ShipInputLateCount(slots.Player);
        var behind = slots.MissingRun >= BehindMissing && (late > slots.LateSeen || (hasNewest && newest > slots.NextSeq));
        slots.LateSeen = late;
        if (slots.AheadRun < AheadTicks && !behind)
        {
            return false;
        }

        Stats.Reanchors++;
        if (!_reanchorLogged || tick - _lastReanchorLog >= LogIntervalTicks)
        {
            _reanchorLogged = true;
            _lastReanchorLog = tick;
            _logger.Reanchored(ship.Value, slots.Player.Value, slots.NextSeq, Stats.Reanchors);
        }

        return true;
    }

    /// <summary>Inputs of online players who pilot no ship are dropped once per tick and counted (NotPilot, no reply).</summary>
    private void DropNonPilotInputs(ulong tick)
    {
        var state = _simulation.State;
        var players = state.Players.All;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (!player.IsOnline || IsPilot(state, player) || !_inputs.TryPeekNewestShipSeq(player.Player, out _))
            {
                continue;
            }

            _inputs.ClearShipInputs(player.Player);
            Stats.NotPilotDropped++;
            if (!_logged || tick - _lastNotPilotLog >= LogIntervalTicks)
            {
                _logged = true;
                _lastNotPilotLog = tick;
                _logger.NotPilotDropped(player.Player.Value, Stats.NotPilotDropped);
            }
        }
    }

    private bool IsPilot(WorldState state, PlayerState player) =>
        player.Seat is { } seat && player.Frame.Kind == FrameKind.Construct
        && state.Constructs.TryGet(new EntityId(player.Frame.Id), out var construct) && construct.Mode == ConstructMode.Ship
        && _stats.TryGet(construct.Id, out var stats) && stats.ControlCell == seat;

    private sealed class Slots
    {
        public PlayerId Player { get; set; }

        public bool Anchored { get; set; }

        public bool HasLast { get; set; }

        public uint NextSeq { get; set; }

        public uint LastProcessedSeq { get; set; }

        public ShipControl Last { get; set; }

        public int MissingRun { get; set; }

        public int AheadRun { get; set; }

        public int LateSeen { get; set; }
    }
}
