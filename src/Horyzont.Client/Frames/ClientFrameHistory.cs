using Horyzont.Client.Replication;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Frames;

/// <summary>
/// World poses of moving construct frames on a client, recorded at server ticks from full snapshots (K5-20). Only ship-mode constructs
/// travel in snapshots, so every construct sample of a full snapshot is a ship; its local pose is composed with the parent frame at
/// the same tick (system: identity, planet: placement, construct: this history). A despawned construct is forgotten. Register it as a
/// sink of the snapshot decoder after the entity mirror; main thread only, no allocation per snapshot after warm-up.
/// </summary>
public sealed class ClientFrameHistory : IFullSnapshotSink, IFrameHistoryView
{
    private readonly EntityMirror _entities;
    private readonly IPlanetPlacement _placement;
    private readonly FramePoseHistory _history;

    /// <summary>Initializes a new instance of the <see cref="ClientFrameHistory"/> class and follows despawns of the mirror.</summary>
    /// <param name="entities">The entity mirror (kinds of snapshot entities, despawns).</param>
    /// <param name="placement">Planet poses in their system frame.</param>
    /// <param name="capacity">Number of frames recorded at once.</param>
    public ClientFrameHistory(EntityMirror entities, IPlanetPlacement placement, int capacity = FramePoseHistory.DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(placement);
        _entities = entities;
        _placement = placement;
        _history = new FramePoseHistory(capacity);
        _entities.Despawned += OnDespawned;
    }

    /// <summary>Gets the number of recorded frames.</summary>
    public int Count => _history.Count;

    /// <summary>Whether the frame has recorded poses.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>True when recorded.</returns>
    public bool Contains(FrameRef frame) => _history.Contains(frame);

    /// <inheritdoc />
    public void OnFullSnapshot(Snapshot full)
    {
        ArgumentNullException.ThrowIfNull(full);
        for (var i = 0; i < full.Entities.Count; i++)
        {
            var state = full.Entities[i];
            if (state.FrameIndex >= full.Frames.Count || !_entities.TryGetKind(state.Entity, out var kind) || kind != EntityKind.Construct)
            {
                continue;
            }

            if (TryGetParentWorldPose(full.Frames[state.FrameIndex].Frame, full.ServerTick.Value, out var parent))
            {
                _history.Record(full.ServerTick, FrameRef.Of(state.Entity), FrameMath.Compose(parent, FrameMath.FromFloat(state.Position, state.Rotation)));
            }
        }
    }

    /// <inheritdoc />
    public bool TryGetWorldPose(FrameRef frame, double tick, out PoseD pose) => _history.TryGetWorldPose(frame, tick, out pose);

    /// <summary>Forgets every frame (disconnect).</summary>
    public void Clear() => _history.Clear();

    private bool TryGetParentWorldPose(FrameRef frame, double tick, out PoseD pose)
    {
        switch (frame.Kind)
        {
            case FrameKind.System:
                pose = PoseD.Identity;
                return true;
            case FrameKind.Planet:
                return _placement.TryGetPose(new PlanetId(frame.Id), out pose);
            case FrameKind.Construct:
                return _history.TryGetWorldPose(frame, tick, out pose);
            default:
                pose = PoseD.Identity;
                return false;
        }
    }

    private void OnDespawned(EntityId id) => _history.Forget(FrameRef.Of(id));
}
