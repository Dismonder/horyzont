using Horyzont.Client.Frames;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;

namespace Horyzont.Client.Navigation;

/// <summary>Adapter of <see cref="ClientFrameGraph"/> to <see cref="IWorldPoseSource"/>.</summary>
/// <param name="graph">The client frame graph.</param>
public sealed class ClientFramePoses(ClientFrameGraph graph) : IWorldPoseSource
{
    /// <inheritdoc />
    public bool TryGetWorldPose(FrameRef frame, double renderTick, out PoseD pose) => graph.TryGetWorldPose(frame, renderTick, out pose);
}
