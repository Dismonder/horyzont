using Horyzont.Core.Frames;
using Horyzont.Core.Ids;

namespace Horyzont.Client.Navigation;

/// <summary>World poses (system frame) of reference frames at a render tick; <c>ClientFrameGraph</c> through <see cref="ClientFramePoses"/>.</summary>
public interface IWorldPoseSource
{
    /// <summary>Gets the pose of a frame in the system frame.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="renderTick">Server tick being rendered (fractional).</param>
    /// <param name="pose">Receives the world pose.</param>
    /// <returns>False for an unknown frame or a missing pose.</returns>
    bool TryGetWorldPose(FrameRef frame, double renderTick, out PoseD pose);
}
