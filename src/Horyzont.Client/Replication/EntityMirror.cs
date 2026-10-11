using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Replication;

/// <summary>
/// Read-only client entity poses and player ownership, independent of physical proxies. Snapshot samples whose parent counter is
/// newer than the known one (or whose entity has no spawn yet) wait, at most <see cref="MaxParkedSamples"/> per entity, until the
/// reparent operation or the spawn arrives; an overflow asks for a resynchronisation. Older counters are dropped.
/// </summary>
public sealed class EntityMirror(TimeProvider? time = null) : IFullSnapshotSink
{
    /// <summary>Most samples one entity may park while its parent counter is ahead of the known one.</summary>
    public const int MaxParkedSamples = 32;

    private const int MaxFrameDepth = 8;

    private readonly Dictionary<EntityId, EntitySpawn> _entities = [];
    private readonly Dictionary<EntityId, Pose> _poses = [];
    private readonly Dictionary<EntityId, CellPos> _seats = [];
    private readonly List<EntityId> _ids = [];
    private readonly Dictionary<EntityId, List<Sample>> _parked = [];
    private readonly ResyncTracker _resync = new(time ?? TimeProvider.System);
    private readonly List<EntityId> _due = [];

    /// <summary>Raised once per entity when parked samples overflow; the owner sends a resynchronisation request.</summary>
    public event Action<EntityId>? ResyncNeeded;

    /// <summary>Raised after an entity was spawned or its spawn repeated (respawn, resync); views create or refresh their node.</summary>
    public event Action<EntitySpawn>? Spawned;

    /// <summary>Raised after an entity left the mirror; views free their node.</summary>
    public event Action<EntityId>? Despawned;

    /// <summary>Gets the number of known entities.</summary>
    public int Count => _ids.Count;

    /// <summary>Gets the known entities in identifier order.</summary>
    public IReadOnlyList<EntityId> Ids => _ids;

    /// <summary>
    /// Gets the number of samples applied although their frame differs from the known frame of the same parent counter (K5-20
    /// diagnostic; the acceptance tests require zero: a parent counter names exactly one frame).
    /// </summary>
    public int WrongFrameApplied { get; private set; }

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
                // K5-26: a player's kind data is its seat; data from the network, validated before anything changes.
                var seat = spawn.Kind == EntityKind.Player ? EntitySpawnSeat.Decode(spawn.KindData, spawn.Frame.Frame) : null;
                if (seat is { } cell)
                {
                    _seats[spawn.Entity] = cell;
                }
                else
                {
                    _seats.Remove(spawn.Entity);
                }

                if (!_entities.ContainsKey(spawn.Entity))
                {
                    var at = _ids.BinarySearch(spawn.Entity, IdComparer.Instance);
                    _ids.Insert(~at, spawn.Entity);
                }

                _entities[spawn.Entity] = spawn;
                _poses[spawn.Entity] = new Pose(spawn.Frame, spawn.Initial);
                _resync.Remove(spawn.Entity);
                Replay(spawn.Entity);
                Spawned?.Invoke(Merged(spawn.Entity));
                break;
            case EntityDespawn despawn:
                Remove(despawn.Entity);
                break;
            case ConstructRemoved removed:
                Remove(removed.Construct);
                break;
            case OperationApplied { Operation: EntityReparentOp op }:
                if (_poses.TryGetValue(op.Entity, out var known) && op.NewParentSeq == unchecked(known.State.ParentSeq + 1))
                {
                    if (op.SeatCell is { } seatCell)
                    {
                        _seats[op.Entity] = seatCell;
                    }
                    else
                    {
                        _seats.Remove(op.Entity);
                    }

                    _poses[op.Entity] = new Pose(
                        new SnapshotFrame(op.NewParent, op.NewParentSeq),
                        known.State with
                        {
                            ParentSeq = op.NewParentSeq,
                            Position = op.LocalPosition,
                            Rotation = op.LocalRotation,
                            Velocity = op.LocalVelocity
                        });
                    Replay(op.Entity);
                }

                break;
        }
    }

    /// <inheritdoc />
    public void OnFullSnapshot(Snapshot full)
    {
        ArgumentNullException.ThrowIfNull(full);
        for (var i = 0; i < full.Entities.Count; i++)
        {
            var state = full.Entities[i];
            if (state.FrameIndex >= full.Frames.Count)
            {
                continue;
            }

            Offer(new Sample(full.Frames[state.FrameIndex], state));
        }
    }

    /// <summary>Looks up the latest complete lifecycle state. Allocates a record; per-frame readers use <see cref="TryGetPose"/>.</summary>
    /// <param name="id">Entity.</param>
    /// <param name="spawn">State and ownership.</param>
    /// <returns>Whether known.</returns>
    public bool TryGet(EntityId id, out EntitySpawn? spawn)
    {
        if (_entities.ContainsKey(id))
        {
            spawn = Merged(id);
            return true;
        }

        spawn = null;
        return false;
    }

    /// <summary>Looks up the newest frame and state sample of an entity, without allocating.</summary>
    /// <param name="id">Entity.</param>
    /// <param name="frame">Frame of the sample.</param>
    /// <param name="state">Newest state.</param>
    /// <returns>Whether known.</returns>
    public bool TryGetPose(EntityId id, out SnapshotFrame frame, out EntityState state)
    {
        if (_poses.TryGetValue(id, out var pose))
        {
            frame = pose.Frame;
            state = pose.State;
            return true;
        }

        frame = default;
        state = default;
        return false;
    }

    /// <summary>The seat of a player (K5-26): from its spawn, then from live reparents. Does not allocate.</summary>
    /// <param name="id">Entity.</param>
    /// <param name="seat">Receives the seat cell in the player's construct frame.</param>
    /// <returns>False when the entity is unknown or does not sit.</returns>
    public bool TryGetSeat(EntityId id, out CellPos seat) => _seats.TryGetValue(id, out seat);

    /// <summary>Looks up the kind of a spawned entity, without allocating.</summary>
    /// <param name="id">Entity.</param>
    /// <param name="kind">Receives the kind.</param>
    /// <returns>Whether known.</returns>
    public bool TryGetKind(EntityId id, out EntityKind kind)
    {
        if (_entities.TryGetValue(id, out var spawn))
        {
            kind = spawn.Kind;
            return true;
        }

        kind = default;
        return false;
    }

    /// <summary>Converts the newest sample of an entity into the frame of its planet, following construct frames. Does not allocate.</summary>
    /// <param name="id">Entity.</param>
    /// <param name="constructs">Construct copies for the conversion; without them only planet frames convert.</param>
    /// <param name="planet">Planet of the entity.</param>
    /// <param name="position">Position in the planet frame.</param>
    /// <param name="rotation">Rotation in the planet frame.</param>
    /// <returns>False when the entity is unknown or its construct chain is not available.</returns>
    public bool TryGetPlanetPose(EntityId id, ConstructMirror? constructs, out PlanetId planet, out Vector3 position, out Quaternion rotation)
    {
        planet = default;
        position = default;
        rotation = Quaternion.Identity;
        if (!_poses.TryGetValue(id, out var pose))
        {
            return false;
        }

        var frame = pose.Frame.Frame;
        position = pose.State.Position;
        rotation = pose.State.Rotation;
        for (var depth = 0; depth <= MaxFrameDepth; depth++)
        {
            switch (frame.Kind)
            {
                case FrameKind.Planet:
                    planet = new PlanetId(frame.Id);
                    return true;
                case FrameKind.Construct when constructs is not null && constructs.TryGetConstruct(new EntityId(frame.Id), out var construct):
                    position = construct.LocalToParent(position);
                    rotation = Quaternion.Concatenate(rotation, construct.Rotation);
                    frame = construct.Parent;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>Copies other players' feet in one planet frame, without allocating.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="own">Local player to exclude.</param>
    /// <param name="destination">Caller buffer.</param>
    /// <param name="constructs">Construct copies, to convert players standing on constructs.</param>
    /// <returns>Number copied.</returns>
    public int CopyOtherPlayerFeet(PlanetId planet, PlayerId own, Span<Vector3> destination, ConstructMirror? constructs = null)
    {
        var count = 0;
        for (var i = 0; i < _ids.Count; i++)
        {
            var entity = _entities[_ids[i]];
            if (entity.Kind == EntityKind.Player && entity.Owner != own
                && TryGetPlanetPose(entity.Entity, constructs, out var entityPlanet, out var feet, out _) && entityPlanet == planet)
            {
                if (count == destination.Length)
                {
                    break;
                }

                destination[count++] = feet;
            }
        }

        return count;
    }

    /// <summary>Repeats the resync requests that the server has not answered (it may have refused them silently).</summary>
    public void RetryResyncs()
    {
        _due.Clear();
        _resync.CollectDue(_due);
        foreach (var id in _due)
        {
            ResyncNeeded?.Invoke(id);
        }
    }

    /// <summary>Clears a disconnected session.</summary>
    public void Clear()
    {
        var ids = _ids.ToArray();
        _entities.Clear();
        _poses.Clear();
        _seats.Clear();
        _ids.Clear();
        _parked.Clear();
        _resync.Clear();
        foreach (var id in ids)
        {
            Despawned?.Invoke(id);
        }
    }

    private static bool IsOlder(uint value, uint known) => unchecked((int)(value - known)) < 0;

    private void Remove(EntityId id)
    {
        _poses.Remove(id);
        _seats.Remove(id);
        if (_entities.Remove(id))
        {
            _ids.Remove(id);
            Despawned?.Invoke(id);
        }

        _parked.Remove(id);
        _resync.Remove(id);
    }

    private void Offer(Sample sample)
    {
        var id = sample.State.Entity;
        if (_poses.TryGetValue(id, out var known))
        {
            if (known.State.ParentSeq == sample.State.ParentSeq)
            {
                CountWrongFrame(known.Frame, sample.Frame);
                _poses[id] = new Pose(sample.Frame, sample.State with { FrameIndex = 0 });
                return;
            }

            if (IsOlder(sample.State.ParentSeq, known.State.ParentSeq))
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
        if (!_parked.TryGetValue(id, out var list) || !_poses.TryGetValue(id, out var known))
        {
            return;
        }

        var seq = known.State.ParentSeq;
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
            CountWrongFrame(known.Frame, sampleToApply.Frame);
            _poses[id] = new Pose(sampleToApply.Frame, sampleToApply.State with { FrameIndex = 0 });
        }
    }

    private void CountWrongFrame(SnapshotFrame known, SnapshotFrame sample)
    {
        if (known.Frame != sample.Frame)
        {
            WrongFrameApplied++;
        }
    }

    private EntitySpawn Merged(EntityId id)
    {
        var spawn = _entities[id];
        var pose = _poses[id];
        return spawn with { Frame = pose.Frame, Initial = pose.State };
    }

    private readonly record struct Pose(SnapshotFrame Frame, EntityState State);

    private readonly record struct Sample(SnapshotFrame Frame, EntityState State);

    private sealed class IdComparer : IComparer<EntityId>
    {
        public static readonly IdComparer Instance = new();

        public int Compare(EntityId x, EntityId y) => x.Value.CompareTo(y.Value);
    }
}
