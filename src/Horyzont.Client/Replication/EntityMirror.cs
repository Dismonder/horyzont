using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Replication;

/// <summary>Read-only client entity poses and player ownership, independent of physical proxies.</summary>
public sealed class EntityMirror : IFullSnapshotSink
{
    private readonly Dictionary<EntityId, EntitySpawn> _entities = [];
    private readonly List<EntityId> _ids = [];

    /// <summary>Applies reliable lifecycle or reparent events.</summary>
    /// <param name="message">Reliable event.</param>
    public void Apply(Message message)
    {
        switch (message)
        {
            case EntitySpawn spawn:
                if (!_entities.ContainsKey(spawn.Entity))
                {
                    _ids.Add(spawn.Entity);
                    _ids.Sort(static (a, b) => a.Value.CompareTo(b.Value));
                }

                _entities[spawn.Entity] = spawn;
                break;
            case EntityDespawn despawn:
                _entities.Remove(despawn.Entity);
                _ids.Remove(despawn.Entity);
                break;
            case ConstructRemoved removed:
                _entities.Remove(removed.Construct);
                _ids.Remove(removed.Construct);
                break;
            case OperationApplied { Operation: EntityReparentOp op }:
                if (_entities.TryGetValue(op.Entity, out var known) && op.NewParentSeq == unchecked(known.Initial.ParentSeq + 1))
                {
                    _entities[op.Entity] = known with
                    {
                        Frame = new SnapshotFrame(op.NewParent, 0),
                        Initial = known.Initial with { ParentSeq = op.NewParentSeq, Position = op.LocalPosition,
                            Rotation = op.LocalRotation, Velocity = op.LocalVelocity },
                    };
                }

                break;
        }
    }

    /// <inheritdoc />
    public void OnFullSnapshot(Snapshot full)
    {
        ArgumentNullException.ThrowIfNull(full);
        foreach (var state in full.Entities)
        {
            if (_entities.TryGetValue(state.Entity, out var known) && known.Initial.ParentSeq == state.ParentSeq)
            {
                _entities[state.Entity] = known with { Frame = full.Frames[state.FrameIndex], Initial = state with { FrameIndex = 0 } };
            }
        }
    }

    /// <summary>Looks up the latest complete lifecycle state.</summary>
    /// <param name="id">Entity.</param>
    /// <param name="spawn">State and ownership.</param>
    /// <returns>Whether known.</returns>
    public bool TryGet(EntityId id, out EntitySpawn? spawn) => _entities.TryGetValue(id, out spawn);

    /// <summary>Copies other players' feet in one planet frame, without allocating.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="own">Local player to exclude.</param>
    /// <param name="destination">Caller buffer.</param>
    /// <returns>Number copied.</returns>
    public int CopyOtherPlayerFeet(PlanetId planet, PlayerId own, Span<Vector3> destination)
    {
        var count = 0;
        foreach (var id in _ids)
        {
            var entity = _entities[id];
            if (entity.Kind == EntityKind.Player && entity.Owner != own && entity.Frame.Frame == FrameRef.Of(planet))
            {
                if (count == destination.Length)
                {
                    break;
                }

                destination[count++] = entity.Initial.Position;
            }
        }

        return count;
    }

    /// <summary>Clears a disconnected session.</summary>
    public void Clear()
    {
        _entities.Clear();
        _ids.Clear();
    }
}
