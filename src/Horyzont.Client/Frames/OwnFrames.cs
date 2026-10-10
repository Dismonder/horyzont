using Horyzont.Client.Movement;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;

namespace Horyzont.Client.Frames;

/// <summary>
/// Own entity across reparents (K5-20, decision 14): a predicted state is re-expressed in the new frame at the tick of the reparent,
/// and a player in a construct frame is attached — frozen, without simulating its input, its pose = frame ∘ local pose. Pure.
/// </summary>
public static class OwnFrames
{
    /// <summary>Whether the own state is attached to a construct frame.</summary>
    /// <param name="own">The validated own state.</param>
    /// <returns>True in a construct frame.</returns>
    public static bool IsAttached(in OwnState own) => own.Frame.Kind == FrameKind.Construct;

    /// <summary>Whether the client simulates its own input for this state (not attached and not frozen by the server).</summary>
    /// <param name="own">The validated own state.</param>
    /// <returns>True when the input is predicted.</returns>
    public static bool SimulatesInput(in OwnState own) => !IsAttached(own) && own.Mode != MovementMode.Frozen;

    /// <summary>Re-expresses a predicted state in another frame at a tick; in a construct frame it becomes attached (frozen, at rest).</summary>
    /// <param name="view">World poses of frames at server ticks (strict view of <see cref="ClientFrameGraph"/>).</param>
    /// <param name="from">Frame of the predicted state.</param>
    /// <param name="to">New frame.</param>
    /// <param name="tick">Server tick of the reparent.</param>
    /// <param name="tickSeconds">Length of a server tick in seconds.</param>
    /// <param name="state">Predicted state in <paramref name="from"/>.</param>
    /// <param name="rebased">Receives the state in <paramref name="to"/>.</param>
    /// <returns>False when a frame has no pose at the tick or the result does not fit the new frame.</returns>
    public static bool TryRebase(IFrameHistoryView view, FrameRef from, FrameRef to, double tick, double tickSeconds, in PredictedState state,
        out PredictedState rebased)
    {
        rebased = state;
        if (!FrameRebase.TryRebase(view, from, to, tick, tickSeconds, state.Feet, state.BodyRotation, state.Velocity,
            out var feet, out var rotation, out var velocity))
        {
            return false;
        }

        rebased = to.Kind == FrameKind.Construct
            ? state with { Feet = feet, BodyRotation = rotation, Velocity = default, Mode = MovementMode.Frozen, OnFloor = false }
            : state with { Feet = feet, BodyRotation = rotation, Velocity = velocity };
        return true;
    }

    /// <summary>World pose of the own state at a (render) tick: its frame at that tick composed with its local pose.</summary>
    /// <param name="view">World poses of frames.</param>
    /// <param name="own">The validated own state.</param>
    /// <param name="tick">Server tick to place the frame at.</param>
    /// <param name="pose">Receives the world pose.</param>
    /// <returns>False when the frame has no pose at the tick.</returns>
    public static bool TryGetWorldPose(IFrameHistoryView view, in OwnState own, double tick, out PoseD pose)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!view.TryGetWorldPose(own.Frame, tick, out var frame))
        {
            pose = PoseD.Identity;
            return false;
        }

        pose = FrameMath.Compose(frame, FrameMath.FromFloat(own.Feet, own.Rotation));
        return true;
    }

    /// <summary>
    /// Pose of the own state in the frame of its planet at a render tick (the body of an attached player lives in planet coordinates):
    /// the frame at that tick (ships interpolated, newest pose past the history) composed with the local pose.
    /// </summary>
    /// <param name="graph">Client frames.</param>
    /// <param name="own">The validated own state.</param>
    /// <param name="renderTick">Server tick being rendered.</param>
    /// <param name="feet">Receives the feet position in the planet frame.</param>
    /// <param name="rotation">Receives the rotation in the planet frame.</param>
    /// <returns>False when a frame is unknown or the pose does not fit the planet frame.</returns>
    public static bool TryToPlanet(ClientFrameGraph graph, in OwnState own, double renderTick, out System.Numerics.Vector3 feet,
        out System.Numerics.Quaternion rotation)
    {
        ArgumentNullException.ThrowIfNull(graph);
        feet = default;
        rotation = System.Numerics.Quaternion.Identity;
        if (!graph.TryGetWorldPose(own.Frame, renderTick, out var frame) || !graph.TryGetWorldPose(FrameRef.Of(own.Planet), renderTick, out var planet))
        {
            return false;
        }

        var local = FrameMath.Relative(planet, FrameMath.Compose(frame, FrameMath.FromFloat(own.Feet, own.Rotation)));
        return FrameMath.TryToFloat(local, FrameKind.Planet, out feet, out rotation);
    }
}
