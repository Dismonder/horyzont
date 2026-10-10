using Horyzont.Core.Ids;
using Horyzont.Core.World;

namespace Horyzont.Core.Frames;

/// <summary>Frame poses of the server world state: planets placed by <see cref="IPlanetPlacement"/>, constructs by their header pose.</summary>
public sealed class WorldStateFramePoses : IFramePoseSource
{
    private readonly WorldState _state;
    private readonly IPlanetPlacement _placement;

    /// <summary>Creates the source.</summary>
    /// <param name="state">World state.</param>
    /// <param name="placement">Planet placement.</param>
    public WorldStateFramePoses(WorldState state, IPlanetPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(placement);
        _state = state;
        _placement = placement;
    }

    /// <summary>Gets the planet placement.</summary>
    public IPlanetPlacement Placement => _placement;

    /// <inheritdoc />
    public bool TryGetParent(FrameRef frame, out FrameRef parent)
    {
        parent = default;
        switch (frame.Kind)
        {
            case FrameKind.System:
                return _state.FrameExists(frame);
            case FrameKind.Planet when _state.Planets.TryGet(new PlanetId(frame.Id), out var planet):
                parent = FrameRef.Of(planet.System);
                return true;
            case FrameKind.Construct when _state.Constructs.TryGet(new EntityId(frame.Id), out var construct):
                parent = construct.Parent;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    public bool TryGetLocalPose(FrameRef frame, out FrameRef parent, out PoseD poseInParent)
    {
        poseInParent = PoseD.Identity;
        if (!TryGetParent(frame, out parent))
        {
            return false;
        }

        switch (frame.Kind)
        {
            case FrameKind.Planet:
                return _placement.TryGetPose(new PlanetId(frame.Id), out poseInParent);
            case FrameKind.Construct:
                _state.Constructs.TryGet(new EntityId(frame.Id), out var construct);
                poseInParent = FrameMath.FromFloat(construct!.Origin, construct.Rotation);
                return true;
            default:
                return true;
        }
    }
}
