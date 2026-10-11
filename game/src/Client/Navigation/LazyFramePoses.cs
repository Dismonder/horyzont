using Horyzont.Client.Frames;
using Horyzont.Client.Navigation;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;

namespace Horyzont.Game.Client.Navigation;

/// <summary>World poses of frames from the client frames; the graph exists only after the first server hello, so it is read on every call.</summary>
/// <param name="frames">The client frames of the session.</param>
public sealed class LazyFramePoses(ClientFrames frames) : IWorldPoseSource
{
    private readonly ClientFrames _frames = frames ?? throw new ArgumentNullException(nameof(frames));

    /// <inheritdoc />
    public bool TryGetWorldPose(FrameRef frame, double renderTick, out PoseD pose)
    {
        if (_frames.Graph is { } graph)
        {
            return graph.TryGetWorldPose(frame, renderTick, out pose);
        }

        pose = PoseD.Identity;
        return false;
    }
}
