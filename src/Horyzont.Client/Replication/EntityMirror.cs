using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Replication;

/// <summary>
/// Read-only client entity poses and player ownership, independent of physical proxies. Snapshot samples whose parent counter is
/// newer than the known one (or whose entity has no spawn yet) wait, at most <see cref="MaxParkedSamples"/> per entity, until the
/// reparent operation or the spawn arrives; an overflow asks for a resynchronisation. Older counters are dropped.
/// </summary>
public sealed class EntityMirror : IFullSnapshotSink
{
    /// <summary>Most samples one entity may park while its parent counter is ahead of the known one.</summary>
    public const int MaxParkedSamples = 32;

    private readonly Dictionary<EntityId, EntitySpawn> _entities = [];
    private readonly List<EntityId> _ids = [];
    private readonly Dictionary<EntityId, List<Sample>> _parked = [];
    private readonly HashSet<EntityId> _resync = [];

    /// <summary>Raised once per entity when parked samples overflow; the owner sends a resynchronisation request.</summary>
    public event Action<EntityId>? ResyncNeeded;

    /// <summary>Gets the number of known entities.</summary>
    public int Count => _ids.Count;

    /// <summary>Gets the number of samples parked for entities (bounded by <see cref="MaxParkedSamples"/> each).</summary>
    public int ParkedSamples
    {
        get
        {
            var total = 0;
            foreach (var list in _parked.Values)
            {
                total += list.Count;
            }

            return total;
        }
    }

    /// <summary>Applies reliable lifecycle or reparent events.</summary>
    /// <param name="message">Reliable event.</param>
    public void Apply(Message message)
    {
        switch (message)
        {
            case EntitySpawn spawn:
                if (!_entities.ContainsKey(spawn.Entity))
                {
                    var at = _ids.BinarySearch(spawn.Entity, IdComparer.Instance);
                    _ids.Insert(~at, spawn.Entity);
                }

                _entities[spawn.Entity] = spawn;
                _resync.Remove(spawn.Entity);
                Replay(spawn.Entity);
                break;
            case EntityDespawn despawn:
                Remove(despawn.Entity);
                break;
            case ConstructRemoved removed:
                Remove(removed.Construct);
                break;
            case OperationApplied { Operation: EntityReparentOp op }:
                if (_entities.TryGetValue(op.Entity, out var known) && op.NewParentSeq == unchecked(known.Initial.ParentSeq + 1))
                {
                    _entities[op.Entity] = known with
                    {
                        Frame = new SnapshotFrame(op.NewParent, op.NewParentSeq),
                        Initial = known.Initial with
                        {
                            ParentSeq = op.NewParentSeq,
                            Position = op.LocalPosition,
                            Rotation = op.LocalRotation,
                            Velocity = op.LocalVelocity
                        },
                    };
                    Replay(op.Entity);
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
            if (state.FrameIndex >= full.Frames.Count)
            {
                continue;
            }

            Offer(new Sample(full.Frames[state.FrameIndex], state));
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
        for (var i = 0; i < _ids.Count; i++)
        {
            var entity = _entities[_ids[i]];
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
        _parked.Clear();
        _resync.Clear();
    }

    private static bool IsOlder(uint value, uint known) => unchecked((int)(value - known)) < 0;

    private void Remove(EntityId id)
    {
        if (_entities.Remove(id))
        {
            _ids.Remove(id);
        }

        _parked.Remove(id);
        _resync.Remove(id);
    }

    private void Offer(Sample sample)
    {
        var id = sample.State.Entity;
        if (_entities.TryGetValue(id, out var known))
        {
            if (known.Initial.ParentSeq == sample.State.ParentSeq)
            {
                _entities[id] = known with { Frame = sample.Frame, Initial = sample.State with { FrameIndex = 0 } };
                return;
            }

            if (IsOlder(sample.State.ParentSeq, known.Initial.ParentSeq))
            {
                return;
            }
        }

        if (!_parked.TryGetValue(id, out var list))
        {
            list = [];
            _parked.Add(id, list);
        }

        if (list.Count == MaxParkedSamples)
        {
            list.Clear();
            if (_resync.Add(id))
            {
                ResyncNeeded?.Invoke(id);
            }

            return;
        }

        list.Add(sample);
    }

    private void Replay(EntityId id)
    {
        if (!_parked.TryGetValue(id, out var list) || !_entities.TryGetValue(id, out var known))
        {
            return;
        }

        var seq = known.Initial.ParentSeq;
        Sample? latest = null;
        var keep = new List<Sample>();
        foreach (var sample in list)
        {
            if (sample.State.ParentSeq == seq)
            {
                latest = sample;
            }
            else if (!IsOlder(sample.State.ParentSeq, seq))
            {
                keep.Add(sample);
            }
        }

        if (keep.Count == 0)
        {
            _parked.Remove(id);
        }
        else
        {
            list.Clear();
            list.AddRange(keep);
        }

        if (latest is { } sampleToApply)
        {
            _entities[id] = known with { Frame = sampleToApply.Frame, Initial = sampleToApply.State with { FrameIndex = 0 } };
        }
    }

    private readonly record struct Sample(SnapshotFrame Frame, EntityState State);

    private sealed class IdComparer : IComparer<EntityId>
    {
        public static readonly IdComparer Instance = new();

        public int Compare(EntityId x, EntityId y) => x.Value.CompareTo(y.Value);
    }
}
