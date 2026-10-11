using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;

namespace Horyzont.Server.Replication;

/// <summary>Bounded snapshot selection and encoding against a retained acknowledged baseline.</summary>
public sealed class SnapshotBuilder(ISnapshotBudget budget)
{
    private readonly Dictionary<PlayerId, Connection> _connections = [];
    private readonly SnapshotFrame[] _frames = new SnapshotFrame[8];
    private readonly EntityState[] _delta = new EntityState[22];
    private readonly EntityState[] _full = new EntityState[22];
    private readonly List<EntityId> _stale = [];
    private readonly int[] _order = new int[22];
    private readonly int[] _chain = new int[8];
    private SnapshotCandidate[] _candidates = [];

    /// <summary>Copies the visible set. Capacity changes allocate only when the interest set grows.</summary>
    /// <param name="player">Recipient.</param>
    /// <param name="entities">Visible entities.</param>
    public void SetEntities(PlayerId player, ReadOnlySpan<ReplicationEntity> entities)
    {
        if (!_connections.TryGetValue(player, out var connection))
        {
            connection = new Connection();
            _connections.Add(player, connection);
        }

        if (connection.Entities.Length < entities.Length)
        {
            connection.Entities = new ReplicationEntity[Math.Max(entities.Length, connection.Entities.Length * 2)];
        }

        entities.CopyTo(connection.Entities);
        connection.Count = entities.Length;
        foreach (var entity in entities)
        {
            connection.LastSent.TryAdd(entity.Id, 0);
        }

        if (connection.LastSent.Count > (entities.Length * 2) + 16)
        {
            // Entities that left the set keep no age: forget them so the table follows the interest set.
            _stale.Clear();
            foreach (var id in connection.LastSent.Keys)
            {
                var present = false;
                foreach (var entity in entities)
                {
                    if (entity.Id == id)
                    {
                        present = true;
                        break;
                    }
                }

                if (!present)
                {
                    _stale.Add(id);
                }
            }

            foreach (var id in _stale)
            {
                connection.LastSent.Remove(id);
            }
        }
    }

    /// <summary>Acknowledges only a retained, sent snapshot, with sequence arithmetic across wrapping.</summary>
    /// <param name="player">Recipient.</param>
    /// <param name="id">Acknowledged identifier.</param>
    /// <returns>Whether the baseline was accepted.</returns>
    public bool Acknowledge(PlayerId player, uint id)
    {
        if (!_connections.TryGetValue(player, out var connection) || id == 0
            || connection.History[id % 64].Id != id
            || (connection.Ack != 0 && unchecked((int)(id - connection.Ack)) <= 0))
        {
            return false;
        }

        connection.Ack = id;
        return true;
    }

    /// <summary>Builds one packet, selecting the own body first and then the rest by the K3-14 scheduling policy (starved entities, then distance over age).</summary>
    /// <param name="player">Recipient.</param>
    /// <param name="tick">Server tick.</param>
    /// <param name="lastInputSeq">Last processed input.</param>
    /// <param name="destination">Caller-owned packet buffer.</param>
    /// <returns>Packet byte count or zero when no state or budget is available.</returns>
    public int Build(PlayerId player, Tick tick, uint lastInputSeq, Span<byte> destination)
    {
        if (!_connections.TryGetValue(player, out var connection) || connection.Count == 0)
        {
            return 0;
        }

        var limit = Math.Min(1150, Math.Min(destination.Length, budget.GetByteBudget(player, tick)));
        var id = unchecked(connection.Next + 1);
        if (id == 0)
        {
            id = 1;
        }

        var baseline = connection.Ack == 0 ? null : connection.History[connection.Ack % 64];
        if (baseline?.Id != connection.Ack || connection.SentCount % 20 == 0)
        {
            baseline = null;
        }

        var baselineId = baseline?.Id ?? 0;
        var size = 2 + VarSize(id) + VarSize(tick.Value) + VarSize(baselineId) + VarSize(lastInputSeq) + 2;
        var frameCount = 0;
        var count = 0;
        var own = -1;
        for (var i = 0; i < connection.Count; i++)
        {
            if (connection.Entities[i].Owner == player)
            {
                own = i;
                break;
            }
        }

        // Order: own body and the constructs of its frame chain (K5-26 decision 6: a passenger's ship is in every snapshot, and its
        // frames take the first places of the frame table), starved entities, then distance over age (K3-14 policy); exact sizes are
        // re-checked while writing.
        var ownState = own >= 0 ? connection.Entities[own] : default;
        var chainCount = own >= 0 ? FindChain(connection, ownState.Frame.Frame) : 0;
        if (_candidates.Length < connection.Count)
        {
            _candidates = new SnapshotCandidate[Math.Max(connection.Count, _candidates.Length * 2)];
        }

        for (var i = 0; i < connection.Count; i++)
        {
            var entity = connection.Entities[i];
            var distance = own >= 0 && entity.Frame.Frame == ownState.Frame.Frame
                ? Vector3.Distance(entity.State.Position, ownState.State.Position)
                : float.PositiveInfinity;
            var rounds = (int)Math.Min(int.MaxValue, connection.SentCount - connection.LastSent[entity.Id]);
            _candidates[i] = new SnapshotCandidate(entity.Id, distance, EntityCost(entity, baseline, i == own), rounds, i == own || InChain(i, chainCount));
        }

        var selectedCount = SnapshotSchedulingPolicy.Select(
            _candidates.AsSpan(0, connection.Count), _order.AsSpan(0, Math.Min(connection.Count, 22)), Math.Max(0, limit - size - (own >= 0 ? FrameCost(ownState.Frame) : 0)));

        for (var attempt = 0; attempt < selectedCount && count < 22; attempt++)
        {
            var selected = _order[attempt];
            var candidate = connection.Entities[selected];
            var frameIndex = -1;
            for (var i = 0; i < frameCount; i++)
            {
                if (_frames[i].Frame == candidate.Frame.Frame)
                {
                    frameIndex = i;
                    break;
                }
            }

            var frameBytes = 0;
            if (frameIndex < 0)
            {
                if (frameCount == 8)
                {
                    continue;
                }

                frameIndex = frameCount;
                frameBytes = FrameCost(candidate.Frame);
            }

            var full = candidate.State with { FrameIndex = (byte)frameIndex, Fields = (EntityStateFields)15 };
            var delta = DeltaOf(full, candidate.Frame, baseline, selected == own);
            var entityBytes = FieldBytes(delta);
            if (size + frameBytes + entityBytes > limit)
            {
                if (selected == own || InChain(selected, chainCount))
                {
                    return 0;
                }

                continue;
            }

            if (frameIndex == frameCount)
            {
                _frames[frameCount++] = candidate.Frame;
            }

            size += frameBytes + entityBytes;
            _full[count] = full;
            _delta[count++] = delta;
        }

        if (count == 0)
        {
            return 0;
        }

        var encoded = SnapshotWriter.Write(destination, id, tick, baselineId, lastInputSeq, _frames.AsSpan(0, frameCount), _delta.AsSpan(0, count));
        var retained = connection.History[id % 64];
        retained.Id = id;
        retained.Count = count;
        _frames.AsSpan(0, frameCount).CopyTo(retained.Frames);
        _full.AsSpan(0, count).CopyTo(retained.Entities);
        connection.Next = id;
        connection.SentCount++;
        for (var i = 0; i < count; i++)
        {
            connection.LastSent[_full[i].Entity] = connection.SentCount;
        }

        budget.Debit(player, encoded);
        return encoded;

    }

    /// <summary>Indices of the constructs of a frame chain present in the visible set, nearest first (at most 8).</summary>
    private int FindChain(Connection connection, FrameRef frame)
    {
        var count = 0;
        while (count < _chain.Length && frame.Kind == FrameKind.Construct)
        {
            var found = -1;
            for (var i = 0; i < connection.Count; i++)
            {
                if (connection.Entities[i].Kind == EntityKind.Construct && connection.Entities[i].Id.Value == frame.Id)
                {
                    found = i;
                    break;
                }
            }

            if (found < 0)
            {
                break;
            }

            _chain[count++] = found;
            frame = connection.Entities[found].Frame.Frame;
        }

        return count;
    }

    private bool InChain(int index, int chainCount)
    {
        for (var i = 0; i < chainCount; i++)
        {
            if (_chain[i] == index)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Releases one disconnected recipient's history.</summary>
    /// <param name="player">Recipient.</param>
    public void Forget(PlayerId player) => _connections.Remove(player);

    private static int FrameCost(SnapshotFrame frame) => 1 + VarSize(frame.Frame.Id) + VarSize(frame.ParentSeq);

    private static int EntityCost(ReplicationEntity entity, Retained? baseline, bool isOwn) =>
        FieldBytes(DeltaOf(entity.State with { Fields = (EntityStateFields)15 }, entity.Frame, baseline, isOwn));

    private static EntityState DeltaOf(EntityState full, SnapshotFrame frame, Retained? baseline, bool isOwn)
    {
        if (isOwn || baseline is null)
        {
            return full;
        }

        for (var i = 0; i < baseline.Count; i++)
        {
            var previous = baseline.Entities[i];
            if (previous.Entity == full.Entity && previous.ParentSeq == full.ParentSeq && baseline.Frames[previous.FrameIndex] == frame)
            {
                return full with { Fields = ChangedFields(full, previous) };
            }
        }

        return full;
    }

    private static int FieldBytes(EntityState delta) =>
        VarSize(delta.Entity.Value) + 2 + VarSize(delta.ParentSeq)
        + ((delta.Fields & EntityStateFields.Position) != 0 ? 12 : 0)
        + ((delta.Fields & EntityStateFields.Rotation) != 0 ? 4 : 0)
        + ((delta.Fields & EntityStateFields.Velocity) != 0 ? 6 : 0)
        + ((delta.Fields & EntityStateFields.MotionFlags) != 0 ? 1 : 0);

    private static EntityStateFields ChangedFields(EntityState current, EntityState previous)
    {
        var fields = EntityStateFields.None;
        if (!Same(current.Position, previous.Position))
        {
            fields |= EntityStateFields.Position;
        }

        if (!Same(current.Rotation.X, previous.Rotation.X) || !Same(current.Rotation.Y, previous.Rotation.Y)
            || !Same(current.Rotation.Z, previous.Rotation.Z) || !Same(current.Rotation.W, previous.Rotation.W))
        {
            fields |= EntityStateFields.Rotation;
        }

        if (!Same(current.Velocity, previous.Velocity))
        {
            fields |= EntityStateFields.Velocity;
        }

        if (current.MotionFlags != previous.MotionFlags)
        {
            fields |= EntityStateFields.MotionFlags;
        }

        return fields;
    }

    private static bool Same(Vector3 a, Vector3 b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);
    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static int VarSize(ulong value)
    {
        var count = 1;
        while (value >= 128)
        {
            count++;
            value >>= 7;
        }

        return count;
    }

    private sealed class Connection
    {
        public Connection()
        {
            for (var i = 0; i < History.Length; i++)
            {
                History[i] = new Retained();
            }
        }

        public ReplicationEntity[] Entities { get; set; } = [];
        public int Count { get; set; }
        public uint Next { get; set; }
        public uint Ack { get; set; }
        public ulong SentCount { get; set; }
        public Retained[] History { get; } = new Retained[64];
        public Dictionary<EntityId, ulong> LastSent { get; } = [];
    }

    private sealed class Retained
    {
        public uint Id { get; set; }
        public int Count { get; set; }
        public SnapshotFrame[] Frames { get; } = new SnapshotFrame[8];
        public EntityState[] Entities { get; } = new EntityState[22];
    }
}
