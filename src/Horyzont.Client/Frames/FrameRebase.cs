using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;

namespace Horyzont.Client.Frames;

/// <summary>
/// Re-expresses a state recorded in one frame in another frame at the same server tick (K5-20, decision 13): the world pose and the
/// world velocity stay the same. Frame poses come from an <see cref="IFrameHistoryView"/> at the state's tick; the velocity of a frame
/// point is a finite difference over one tick of that view. Pure and allocation-free.
/// </summary>
public static class FrameRebase
{
    /// <summary>Expresses a local state of frame <paramref name="from"/> in frame <paramref name="to"/> at a tick.</summary>
    /// <param name="view">World poses of both frames at server ticks.</param>
    /// <param name="from">Frame the state is in.</param>
    /// <param name="to">Frame to express it in.</param>
    /// <param name="tick">Server tick of the state.</param>
    /// <param name="tickSeconds">Length of a server tick in seconds.</param>
    /// <param name="position">Local position in <paramref name="from"/>.</param>
    /// <param name="rotation">Local rotation in <paramref name="from"/>.</param>
    /// <param name="velocity">Local velocity in <paramref name="from"/>.</param>
    /// <param name="newPosition">Receives the position in <paramref name="to"/>.</param>
    /// <param name="newRotation">Receives the rotation in <paramref name="to"/>.</param>
    /// <param name="newVelocity">Receives the velocity in <paramref name="to"/>.</param>
    /// <returns>False when a frame has no pose at the tick or the result does not fit the float range of <paramref name="to"/>.</returns>
    public static bool TryRebase(IFrameHistoryView view, FrameRef from, FrameRef to, double tick, double tickSeconds,
        Vector3 position, Quaternion rotation, Vector3 velocity, out Vector3 newPosition, out Quaternion newRotation, out Vector3 newVelocity)
    {
        ArgumentNullException.ThrowIfNull(view);
        newPosition = default;
        newRotation = Quaternion.Identity;
        newVelocity = default;
        if (!view.TryGetWorldPose(from, tick, out var oldFrame) || !view.TryGetWorldPose(to, tick, out var newFrame))
        {
            return false;
        }

        var world = FrameMath.Compose(oldFrame, FrameMath.FromFloat(position, rotation));
        var local = FrameMath.Relative(newFrame, world);
        if (!FrameMath.TryToFloat(local, to.Kind, out newPosition, out newRotation))
        {
            return false;
        }

        var worldVelocity = PointVelocity(view, from, tick, tickSeconds, oldFrame, world.Position)
            + FrameMath.TransformDirection(oldFrame, Vector3D.FromFloat(velocity));
        var relative = worldVelocity - PointVelocity(view, to, tick, tickSeconds, newFrame, world.Position);
        return FrameMath.TryVectorToFloat(FrameMath.InverseTransformDirection(newFrame, relative), out newVelocity);
    }

    /// <summary>World velocity of the frame point at <paramref name="worldPoint"/>; zero without a neighbouring tick.</summary>
    private static Vector3D PointVelocity(IFrameHistoryView view, FrameRef frame, double tick, double tickSeconds, in PoseD at, Vector3D worldPoint)
    {
        var local = FrameMath.InverseTransformPoint(at, worldPoint);
        if (view.TryGetWorldPose(frame, tick - 1, out var before))
        {
            return (worldPoint - FrameMath.TransformPoint(before, local)) / tickSeconds;
        }

        return view.TryGetWorldPose(frame, tick + 1, out var after)
            ? (FrameMath.TransformPoint(after, local) - worldPoint) / tickSeconds
            : Vector3D.Zero;
    }
}
