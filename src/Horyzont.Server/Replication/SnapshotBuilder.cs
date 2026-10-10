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

    /// <summary>Builds one packet, always selecting the own body first and then the oldest sent visible entities.</summary>
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

        // Selection marks entries in the reusable history of ages; skipped entries keep their old age.
        for (var attempt = 0; attempt < connection.Count && count < 22; attempt++)
        {
            var selected = attempt == 0 ? own : -1;
            if (selected < 0)
            {
                ulong oldest = ulong.MaxValue;
                for (var i = 0; i < connection.Count; i++)
                {
                    var entity = connection.Entities[i];
                    var age = connection.LastSent[entity.Id];
                    if (age < oldest && !connection.Attempted.Contains(entity.Id))
                    {
                        oldest = age;
                        selected = i;
                    }
                }
            }

            if (selected < 0)
            {
                break;
            }

            var candidate = connection.Entities[selected];
            // Prevent retrying a skipped entry in this packet; restore its age below.
            connection.Attempted.Add(candidate.Id);
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
                frameBytes = 1 + VarSize(candidate.Frame.Frame.Id) + VarSize(candidate.Frame.ParentSeq);
            }

            var full = candidate.State with { FrameIndex = (byte)frameIndex, Fields = (EntityStateFields)15 };
            var delta = full;
            if (selected != own && baseline is not null)
            {
                for (var i = 0; i < baseline.Count; i++)
                {
                    var previous = baseline.Entities[i];
                    if (previous.Entity == full.Entity && previous.ParentSeq == full.ParentSeq
                        && baseline.Frames[previous.FrameIndex] == candidate.Frame)
                    {
                        delta = full with { Fields = ChangedFields(full, previous) };
                        break;
                    }
                }
            }

            var entityBytes = VarSize(delta.Entity.Value) + 2 + VarSize(delta.ParentSeq)
                + ((delta.Fields & EntityStateFields.Position) != 0 ? 12 : 0)
                + ((delta.Fields & EntityStateFields.Rotation) != 0 ? 4 : 0)
                + ((delta.Fields & EntityStateFields.Velocity) != 0 ? 6 : 0)
                + ((delta.Fields & EntityStateFields.MotionFlags) != 0 ? 1 : 0);
            if (size + frameBytes + entityBytes > limit)
            {
                if (selected == own)
                {
                    connection.Attempted.Clear();
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

        connection.Attempted.Clear();
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

    /// <summary>Releases one disconnected recipient's history.</summary>
    /// <param name="player">Recipient.</param>
    public void Forget(PlayerId player) => _connections.Remove(player);

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
        public HashSet<EntityId> Attempted { get; } = [];
    }

    private sealed class Retained
    {
        public uint Id { get; set; }
        public int Count { get; set; }
        public SnapshotFrame[] Frames { get; } = new SnapshotFrame[8];
        public EntityState[] Entities { get; } = new EntityState[22];
    }
}
