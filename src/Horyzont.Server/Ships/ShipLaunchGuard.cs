using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Ships;

/// <summary>
/// K5-25 decision 9: at a launch attempt the players in the planet frame of the ship whose capsule touches its hull boxes (plus
/// <see cref="SeatRules.HullMarginMeters"/>) are seated on free passenger, then navigator seats; anyone left (or offline) blocks
/// the launch with <see cref="ShipLaunchBlock.PersonInHull"/>. At most one evaluation per <see cref="SeatRules.LaunchRetryTicks"/>;
/// between evaluations the last answer holds.
/// </summary>
public sealed class ShipLaunchGuard : IShipLaunchGuard
{
    private const int CapsuleSamples = 8;

    private readonly ServerSimulation _simulation;
    private readonly ShipStatsCache _stats;
    private readonly SeatRules _rules;
    private readonly ILogger _logger;
    private readonly Dictionary<EntityId, (ulong Tick, ShipLaunchBlock Block)> _last = [];
    private readonly List<PlayerState> _persons = [];

    /// <summary>Creates the guard.</summary>
    /// <param name="simulation">The server simulation (seating is a server operation).</param>
    /// <param name="stats">Ship stats and hull boxes.</param>
    /// <param name="logger">Logger.</param>
    public ShipLaunchGuard(ServerSimulation simulation, ShipStatsCache stats, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(logger);
        _simulation = simulation;
        _stats = stats;
        _rules = simulation.Rules.Seats;
        _logger = logger;
    }

    /// <summary>Gets the number of evaluations (tests: at most one per retry interval).</summary>
    public int Evaluations { get; private set; }

    /// <inheritdoc />
    public ShipLaunchBlock Evaluate(EntityId ship, Tick tick)
    {
        if (_last.TryGetValue(ship, out var last) && tick.Value - last.Tick < (ulong)Math.Max(1, _rules.LaunchRetryTicks))
        {
            return last.Block;
        }

        var block = Decide(ship);
        _last[ship] = (tick.Value, block);
        Evaluations++;
        return block;
    }

    private ShipLaunchBlock Decide(EntityId shipId)
    {
        var state = _simulation.State;
        if (!state.Constructs.TryGet(shipId, out var ship) || !_stats.TryGet(shipId, out var stats) || !_stats.TryGetHullBoxes(shipId, out var boxes))
        {
            return ShipLaunchBlock.None;
        }

        _persons.Clear();
        var players = state.Players.All;
        for (var i = 0; i < players.Count; i++)
        {
            if (players[i].Frame == ship.Parent && InHull(ship, boxes, players[i].Position))
            {
                _persons.Add(players[i]);
            }
        }

        if (_persons.Count == 0)
        {
            return ShipLaunchBlock.None;
        }

        _persons.Sort(static (a, b) => a.Player.Value.CompareTo(b.Player.Value));
        var blocked = false;
        for (var i = 0; i < _persons.Count; i++)
        {
            var person = _persons[i];
            if (!person.IsOnline || !_rules.AutoSeatOnLaunch || !TrySeat(ship, stats, person))
            {
                blocked = true;
            }
        }

        if (blocked)
        {
            _logger.PersonInHull(shipId.Value);
        }

        return blocked ? ShipLaunchBlock.PersonInHull : ShipLaunchBlock.None;
    }

    private bool TrySeat(Construct ship, ShipStats stats, PlayerState person)
    {
        if (!TryFindSeat(ship, stats, SeatRole.Passenger, out var cell) && !TryFindSeat(ship, stats, SeatRole.Navigator, out cell))
        {
            return false;
        }

        var orientation = ship.GetBlock(cell).Orientation;
        _simulation.ApplyServerOperation(new EntityReparentOp(
            person.Entity, FrameRef.Of(ship.Id), person.ParentSeq + 1, SeatPose.Feet(cell, orientation, _rules), SeatPose.Rotation(orientation),
            Vector3.Zero, cell));
        _logger.AutoSeated(ship.Id.Value, person.Player.Value, cell.X, cell.Y, cell.Z);
        return true;
    }

    private bool TryFindSeat(Construct ship, ShipStats stats, SeatRole role, out CellPos cell)
    {
        var seats = stats.Seats;
        for (var i = 0; i < seats.Count; i++)
        {
            if (seats[i].Role == role && !SeatQueries.TryGetOccupant(_simulation.State, ship.Id, seats[i].Cell, out _))
            {
                cell = seats[i].Cell;
                return true;
            }
        }

        cell = default;
        return false;
    }

    private bool InHull(Construct ship, ShipHullBox[] boxes, Vector3 feet)
    {
        PlayerCapsule.Axis(feet, _simulation.Rules, out var bottom, out var top);
        var a = ship.ParentToLocal(bottom);
        var b = ship.ParentToLocal(top);
        var reach = _simulation.Rules.PlayerCapsuleRadius + _rules.HullMarginMeters;
        for (var s = 0; s < CapsuleSamples; s++)
        {
            var p = Vector3.Lerp(a, b, s / (float)(CapsuleSamples - 1));
            for (var i = 0; i < boxes.Length; i++)
            {
                var min = new Vector3(boxes[i].Min.X, boxes[i].Min.Y, boxes[i].Min.Z);
                var max = new Vector3(boxes[i].Max.X, boxes[i].Max.Y, boxes[i].Max.Z);
                if (Vector3.Distance(Vector3.Clamp(p, min, max), p) <= reach)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
