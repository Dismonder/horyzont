using Horyzont.Core.Ids;

namespace Horyzont.Core.Frames;

/// <summary>
/// Parent links and local poses of reference frames for <see cref="FrameGraph"/>: a planet lies in its system frame (pose from
/// <see cref="IPlanetPlacement"/>), a construct in its parent frame (its origin and rotation), the system frame is the root (its
/// parent is <c>default</c>).
/// </summary>
public interface IFramePoseSource
{
    /// <summary>Parent of an existing frame; <c>default</c> for the root.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="parent">Receives the parent frame.</param>
    /// <returns>False when the frame does not exist.</returns>
    bool TryGetParent(FrameRef frame, out FrameRef parent);

    /// <summary>Parent and pose of a frame in its parent; the root has parent <c>default</c> and the identity pose.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="parent">Receives the parent frame.</param>
    /// <param name="poseInParent">Receives the pose of <paramref name="frame"/> in <paramref name="parent"/>.</param>
    /// <returns>False when the frame does not exist or has no known pose.</returns>
    bool TryGetLocalPose(FrameRef frame, out FrameRef parent, out PoseD poseInParent);
}
