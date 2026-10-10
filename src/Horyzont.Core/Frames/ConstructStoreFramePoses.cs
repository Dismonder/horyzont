using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Frames;

/// <summary>Construct frames only (no planets): poses of constructs relative to an ancestor frame, for the intent validator.</summary>
/// <param name="store">The constructs.</param>
internal readonly struct ConstructStoreFramePoses(ConstructStore store) : IFramePoseSource
{
    /// <inheritdoc />
    public bool TryGetParent(FrameRef frame, out FrameRef parent)
    {
        if (frame.Kind == FrameKind.Construct && store.TryGet(new EntityId(frame.Id), out var construct))
        {
            parent = construct.Parent;
            return true;
        }

        parent = default;
        return false;
    }

    /// <inheritdoc />
    public bool TryGetLocalPose(FrameRef frame, out FrameRef parent, out PoseD poseInParent)
    {
        if (frame.Kind == FrameKind.Construct && store.TryGet(new EntityId(frame.Id), out var construct))
        {
            parent = construct.Parent;
            poseInParent = FrameMath.FromFloat(construct.Origin, construct.Rotation);
            return true;
        }

        parent = default;
        poseInParent = PoseD.Identity;
        return false;
    }
}
