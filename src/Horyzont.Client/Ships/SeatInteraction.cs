using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Ships;

/// <summary>What a press of the seat keys did.</summary>
public enum SeatUseResult : byte
{
    /// <summary>An intent went to the server.</summary>
    Sent = 0,

    /// <summary>No seat or cockpit under the crosshair within reach.</summary>
    NoSeat = 1,

    /// <summary>The session cannot send now.</summary>
    CannotSend = 2,

    /// <summary>Too many intents wait for the server.</summary>
    Waiting = 3,
}

/// <summary>
/// The seat keys (K5-25 decision 23): <c>seat_use</c> (F) sends <see cref="SeatExitIntent"/> while seated (in flight: an ejection) or
/// <see cref="SeatEnterIntent"/> for the seat or cockpit under the crosshair; <c>seat_evict</c> (Shift+F) sends
/// <see cref="SeatEvictIntent"/>. The server decides (reach, occupant, parking); the reply goes through <see cref="PendingIntentTracker"/>.
/// </summary>
public sealed class SeatInteraction
{
    // The server measures from the eye to the cell centre (3 m); the ray hits the near face, up to half a cell diagonal closer.
    private const float CellHalfDiagonal = 0.87f;

    private readonly IToolWorldView _world;
    private readonly IIntentSender _sender;
    private readonly PendingIntentTracker _pending;
    private readonly BlockContentTable _content;
    private readonly IOwnSeatSource _seat;
    private readonly GameRules _rules;

    /// <summary>Initializes a new instance of the <see cref="SeatInteraction"/> class.</summary>
    /// <param name="world">World view of the tools.</param>
    /// <param name="sender">Where intents go.</param>
    /// <param name="pending">Tracker that settles the reply.</param>
    /// <param name="content">Block definitions by server id.</param>
    /// <param name="seat">The own seat.</param>
    /// <param name="rules">Game rules (seat reach).</param>
    public SeatInteraction(IToolWorldView world, IIntentSender sender, PendingIntentTracker pending, BlockContentTable content, IOwnSeatSource seat, GameRules rules)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _seat = seat ?? throw new ArgumentNullException(nameof(seat));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    /// <summary>Gets a value indicating whether the local player sits.</summary>
    public bool Seated => _seat.TryGetOwnSeat(out _, out _);

    /// <summary>Enter the aimed seat, or leave the own seat.</summary>
    /// <param name="aim">Aim of the local player; null when there is no usable aim (seated players have none).</param>
    /// <param name="clientTick">Client tick.</param>
    /// <returns>What happened.</returns>
    public SeatUseResult Use(AimRay? aim, long clientTick)
    {
        if (_seat.TryGetOwnSeat(out var construct, out _))
        {
            return Send(new SeatExitIntent(construct), clientTick);
        }

        return aim is { } ray && TryFindSeat(ray, out var target, out var cell)
            ? Send(new SeatEnterIntent(target, cell, ray.Eye, ray.Direction), clientTick)
            : SeatUseResult.NoSeat;
    }

    /// <summary>Free the aimed seat of an offline player.</summary>
    /// <param name="aim">Aim of the local player.</param>
    /// <param name="clientTick">Client tick.</param>
    /// <returns>What happened.</returns>
    public SeatUseResult Evict(AimRay? aim, long clientTick) =>
        aim is { } ray && !Seated && TryFindSeat(ray, out var target, out var cell)
            ? Send(new SeatEvictIntent(target, cell, ray.Eye, ray.Direction), clientTick)
            : SeatUseResult.NoSeat;

    private SeatUseResult Send(Intent intent, long clientTick)
    {
        if (!_sender.CanSend)
        {
            return SeatUseResult.CannotSend;
        }

        if (_pending.IsFull)
        {
            return SeatUseResult.Waiting;
        }

        var seq = _sender.Submit(intent);
        _pending.Add(seq, intent, default, clientTick);
        return SeatUseResult.Sent;
    }

    private bool TryFindSeat(in AimRay aim, out EntityId construct, out CellPos cell)
    {
        construct = default;
        cell = default;
        var reach = _rules.Seats.ReachMeters + CellHalfDiagonal;
        if (!_world.RaycastConstructs(aim.Planet, aim.Eye, aim.Direction, reach, out var hit) || !_world.TryGetConstruct(hit.Construct, out var found))
        {
            return false;
        }

        var kind = _content.Get(found.GetBlock(hit.Cell).TypeId).Function.Kind;
        if (kind is not (BlockFunctionKind.Cockpit or BlockFunctionKind.Seat))
        {
            return false;
        }

        construct = found.Id;
        cell = hit.Cell;
        return true;
    }
}
