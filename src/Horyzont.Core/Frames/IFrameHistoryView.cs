using Horyzont.Core.Ids;

namespace Horyzont.Core.Frames;

/// <summary>Read access to recorded world poses of moving frames (server: lag compensation, client: rebasing samples).</summary>
public interface IFrameHistoryView
{
    /// <summary>World pose of a frame at a (fractional) tick, interpolated between recorded ticks.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="tick">The tick.</param>
    /// <param name="pose">Receives the pose.</param>
    /// <returns>False when the frame is not recorded or the tick lies outside its window.</returns>
    bool TryGetWorldPose(FrameRef frame, double tick, out PoseD pose);
}
