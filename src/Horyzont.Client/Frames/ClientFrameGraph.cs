using Horyzont.Client.Replication;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Frames;

/// <summary>
/// Reference frames as a client sees them (K5-20): the system frame is the root, planets lie in it at their placement, static
/// constructs at the pose of their header (<see cref="ConstructMirror"/>), ship-mode constructs at their newest snapshot pose
/// (<see cref="EntityMirror"/>) or, for a render tick, at the pose interpolated from the client frame history. As a
/// <see cref="IFramePoseSource"/> it gives the newest poses to <see cref="Graph"/>. Main thread only; no allocation.
/// </summary>
public sealed class ClientFrameGraph : IFramePoseSource
{
    private readonly EntityMirror _entities;
    private readonly ConstructMirror? _constructs;
    private readonly IFrameHistoryView _history;
    private readonly IPlanetPlacement _placement;
    private readonly SystemId _system;

    /// <summary>Initializes a new instance of the <see cref="ClientFrameGraph"/> class.</summary>
    /// <param name="entities">Entity mirror: kinds and newest poses of ship-mode constructs.</param>
    /// <param name="constructs">Construct copies (headers and modes); null when the client has none yet.</param>
    /// <param name="history">World poses of ship frames at server ticks (<see cref="ClientFrameHistory"/>).</param>
    /// <param name="placement">Planet poses in the system frame (K5-20: home only; K5-21: the star system).</param>
    /// <param name="system">The one star system the client knows; its frame is the root.</param>
    public ClientFrameGraph(EntityMirror entities, ConstructMirror? constructs, IFrameHistoryView history, IPlanetPlacement placement, SystemId system)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(placement);
        _entities = entities;
        _constructs = constructs;
        _history = history;
        _placement = placement;
        _system = system;
        Graph = new FrameGraph(this);
    }

    /// <summary>Gets the graph over the newest poses (regions, relative poses).</summary>
    public FrameGraph Graph { get; }

    /// <inheritdoc />
    public bool TryGetParent(FrameRef frame, out FrameRef parent) => TryGetLocalPose(frame, out parent, out _, needPose: false);

    /// <inheritdoc />
    public bool TryGetLocalPose(FrameRef frame, out FrameRef parent, out PoseD poseInParent) =>
        TryGetLocalPose(frame, out parent, out poseInParent, needPose: true);

    /// <summary>
    /// World pose of a frame at a render tick (fractional server tick): ship-mode constructs are interpolated from the history and
    /// fall back to their newest pose outside its window; static frames do not depend on the tick.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <param name="renderTick">Server tick being rendered.</param>
    /// <param name="pose">Receives the world pose.</param>
    /// <returns>False for an unknown frame, a missing pose or a chain deeper than <see cref="FrameGraph.MaxDepth"/>.</returns>
    public bool TryGetWorldPose(FrameRef frame, double renderTick, out PoseD pose)
    {
        var local = PoseD.Identity;
        for (var links = 0; links <= FrameGraph.MaxDepth + 1; links++)
        {
            if (frame.Kind == FrameKind.Construct && !IsStatic(new EntityId(frame.Id))
                && _history.TryGetWorldPose(frame, renderTick, out var world))
            {
                pose = FrameMath.Compose(world, local);
                return true;
            }

            if (!TryGetLocalPose(frame, out var parent, out var inParent))
            {
                break;
            }

            local = FrameMath.Compose(inParent, local);
            if (parent == default)
            {
                pose = local;
                return true;
            }

            frame = parent;
        }

        pose = PoseD.Identity;
        return false;
    }

    private bool IsStatic(EntityId construct) =>
        _constructs is not null && _constructs.TryGetConstruct(construct, out var value) && value.Mode != ConstructMode.Ship;

    private bool TryGetLocalPose(FrameRef frame, out FrameRef parent, out PoseD pose, bool needPose)
    {
        pose = PoseD.Identity;
        parent = default;
        switch (frame.Kind)
        {
            case FrameKind.System:
                return frame.Id == _system.Value;
            case FrameKind.Planet:
                parent = FrameRef.Of(_system);
                return !needPose || _placement.TryGetPose(new PlanetId(frame.Id), out pose);
            case FrameKind.Construct:
                return TryGetConstructPose(new EntityId(frame.Id), out parent, out pose);
            default:
                return false;
        }
    }

    private bool TryGetConstructPose(EntityId id, out FrameRef parent, out PoseD pose)
    {
        Construct? copy = null;
        var hasCopy = _constructs is not null && _constructs.TryGetConstruct(id, out copy);
        if ((!hasCopy || copy!.Mode == ConstructMode.Ship)
            && _entities.TryGetKind(id, out var kind) && kind == EntityKind.Construct && _entities.TryGetPose(id, out var frame, out var state))
        {
            // A ship (or a construct whose copy has not arrived yet): the newest replicated pose, which follows reparents.
            parent = frame.Frame;
            pose = FrameMath.FromFloat(state.Position, state.Rotation);
            return true;
        }

        if (hasCopy)
        {
            parent = copy!.Parent;
            pose = FrameMath.FromFloat(copy.Origin, copy.Rotation);
            return true;
        }

        parent = default;
        pose = PoseD.Identity;
        return false;
    }
}
