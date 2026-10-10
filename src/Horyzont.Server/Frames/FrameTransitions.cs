using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Server.Frames;

/// <summary>
/// Reparents an entity while keeping its pose in the world (K5-20 decision 7): the world pose from the frame graph is expressed in the
/// new frame, the velocity through the world (frame point velocities from <see cref="ServerFrameHistory"/>), narrowed once with
/// <see cref="FrameMath.TryToFloat"/> and applied as a journaled <see cref="EntityReparentOp"/>. EA rules are checked first, because the
/// applier does not know them: a player goes to a planet or a construct (not the system frame, P6); only a ship-mode construct changes
/// frames, and only to a planet or the system frame. Consumers: K5-21 zone borders, K5-25 seats, K6-37 teleports. Main thread.
/// </summary>
public sealed class FrameTransitions
{
    private readonly ServerSimulation _simulation;
    private readonly ServerFrameHistory _history;
    private readonly double _tickSeconds;

    /// <summary>Creates the transitions.</summary>
    /// <param name="simulation">The simulation.</param>
    /// <param name="history">History of moving frames (its graph gives the poses).</param>
    public FrameTransitions(ServerSimulation simulation, ServerFrameHistory history)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(history);
        _simulation = simulation;
        _history = history;
        _tickSeconds = 1.0 / simulation.Rules.TickRate;
    }

    /// <summary>Reparents an entity at rest in its frame (players carry no velocity in Core; a ship moves with its own frame).</summary>
    /// <param name="entity">The entity (a player's entity or a construct).</param>
    /// <param name="newParent">The new frame.</param>
    /// <param name="seat">Seat cell in the new construct frame, or <c>null</c>.</param>
    /// <param name="applied">The applied operation on success.</param>
    /// <param name="failure">Why nothing happened.</param>
    /// <returns>True when the entity was reparented.</returns>
    public bool TryReparent(EntityId entity, FrameRef newParent, CellPos? seat, out AppliedOperation? applied, out FrameTransitionFailure failure) =>
        TryReparent(entity, newParent, seat, Vector3.Zero, out applied, out failure);

    /// <summary>Reparents an entity that moves relative to its current frame with <paramref name="velocityInFrame"/>.</summary>
    /// <param name="entity">The entity (a player's entity or a construct).</param>
    /// <param name="newParent">The new frame.</param>
    /// <param name="seat">Seat cell in the new construct frame, or <c>null</c>.</param>
    /// <param name="velocityInFrame">Velocity of the entity relative to its current frame, in that frame's axes.</param>
    /// <param name="applied">The applied operation on success.</param>
    /// <param name="failure">Why nothing happened.</param>
    /// <returns>True when the entity was reparented.</returns>
    public bool TryReparent(
        EntityId entity, FrameRef newParent, CellPos? seat, Vector3 velocityInFrame, out AppliedOperation? applied, out FrameTransitionFailure failure)
    {
        applied = null;
        failure = Check(entity, newParent, seat, out var oldFrame, out var seq, out var localPose, out var isConstruct);
        if (failure != FrameTransitionFailure.None)
        {
            return false;
        }

        var graph = _history.Graph;
        if (!graph.TryGetWorldPose(oldFrame, out var oldWorld) || !graph.TryGetWorldPose(newParent, out var newWorld))
        {
            failure = FrameTransitionFailure.PoseUnavailable;
            return false;
        }

        // Pose: old frame, then the local pose, gives the world pose; expressed in the new frame.
        var world = FrameMath.Compose(oldWorld, localPose);
        var inNew = FrameMath.Relative(newWorld, world);

        // Velocity in the world: a ship moves with its own (recorded) frame; anything else moves relative to its frame plus the
        // velocity of the frame point it occupies. The new frame's point velocity is taken away again.
        var worldVelocity = isConstruct
            ? _history.PointVelocity(FrameRef.Of(entity), Vector3D.Zero, _tickSeconds)
            : FrameMath.TransformDirection(oldWorld, Vector3D.FromFloat(velocityInFrame))
                + _history.PointVelocity(oldFrame, localPose.Position, _tickSeconds);
        var relative = worldVelocity - _history.PointVelocity(newParent, inNew.Position, _tickSeconds);
        var newVelocity = FrameMath.InverseTransformDirection(newWorld, relative);
        if (!FrameMath.TryToFloat(inNew, newParent.Kind, out var position, out var rotation)
            || !FrameMath.TryVectorToFloat(newVelocity, out var velocity))
        {
            failure = FrameTransitionFailure.OutOfRange;
            return false;
        }

        applied = _simulation.ApplyServerOperation(new EntityReparentOp(entity, newParent, seq + 1, position, rotation, velocity, seat));
        return true;
    }

    // As the applier: a player, online or offline, other than the one entering sits in the cell.
    private bool SeatTakenByOther(FrameRef construct, CellPos cell, PlayerId entering)
    {
        var players = _simulation.State.Players.All;
        for (var i = 0; i < players.Count; i++)
        {
            if (players[i].Frame == construct && players[i].Seat == cell && players[i].Player != entering)
            {
                return true;
            }
        }

        return false;
    }

    private FrameTransitionFailure Check(
        EntityId entity, FrameRef newParent, CellPos? seat, out FrameRef oldFrame, out uint seq, out PoseD localPose, out bool isConstruct)
    {
        var state = _simulation.State;
        oldFrame = default;
        seq = 0;
        localPose = PoseD.Identity;
        isConstruct = false;
        if (state.Players.TryGetByEntity(entity, out var player))
        {
            oldFrame = player.Frame;
            seq = player.ParentSeq;
            localPose = FrameMath.FromFloat(player.Position, player.BodyRotation);
            if (newParent.Kind == FrameKind.System)
            {
                return FrameTransitionFailure.PlayerToSystem;
            }

            if (!state.FrameExists(newParent))
            {
                return FrameTransitionFailure.UnknownFrame;
            }

            if (seat is { } cell
                && (newParent.Kind != FrameKind.Construct
                    || !state.Constructs.TryGet(new EntityId(newParent.Id), out var target)
                    || !cell.IsInBounds || target.GetBlock(cell).IsEmpty
                    || SeatTakenByOther(newParent, cell, player.Player)))
            {
                return FrameTransitionFailure.InvalidSeat;
            }
        }
        else if (state.Constructs.TryGet(entity, out var construct))
        {
            isConstruct = true;
            oldFrame = construct.Parent;
            seq = construct.ParentSeq;
            localPose = FrameMath.FromFloat(construct.Origin, construct.Rotation);
            if (construct.Mode != ConstructMode.Ship)
            {
                return FrameTransitionFailure.ConstructNotShip;
            }

            if (newParent.Kind == FrameKind.Construct)
            {
                return FrameTransitionFailure.ConstructIntoConstruct;
            }

            if (!state.FrameExists(newParent))
            {
                return FrameTransitionFailure.UnknownFrame;
            }

            if (seat is not null)
            {
                return FrameTransitionFailure.InvalidSeat;
            }
        }
        else
        {
            return FrameTransitionFailure.UnknownEntity;
        }

        return seq == uint.MaxValue ? FrameTransitionFailure.NotAllowed : FrameTransitionFailure.None;
    }
}
