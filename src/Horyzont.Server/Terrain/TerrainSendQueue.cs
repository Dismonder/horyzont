using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Terrain;
using Horyzont.Server.Replication;

namespace Horyzont.Server.Terrain;

/// <summary>Latest-version terrain queue with bounded per-player work and allocation-free selection.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "Name required by K3-16 API contract.")]
public sealed class TerrainSendQueue
{
    /// <summary>
    /// After this many live blocks in a row, the next take is a waiting bulk block (when bulk is wanted): sustained digging near the
    /// viewer cannot hold the bulk transfer back for ever, while live edits still get all the other takes.
    /// </summary>
    public const int LiveTakesPerBulk = 4;

    private readonly IReplicationSendBudget _budget;
    private readonly long _frequency;
    private readonly Dictionary<PlayerId, PlayerQueue> _players = [];
    private readonly ByteWriter _writer = new();

    /// <summary>Creates a queue using the reliable budget shared with entity replication.</summary>
    /// <param name="budget">Shared budget.</param>
    /// <param name="timestampFrequency">Injected monotonic clock frequency.</param>
    public TerrainSendQueue(IReplicationSendBudget budget, long timestampFrequency)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);
        _budget = budget;
        _frequency = timestampFrequency;
    }

    /// <summary>Enqueues immutable server data, coalescing the same key to its highest version.</summary>
    /// <param name="player">Remote recipient.</param>
    /// <param name="key">LOD zero key.</param>
    /// <param name="version">Authoritative version.</param>
    /// <param name="immutableBlob">Owned immutable server envelope.</param>
    /// <param name="priorityClass">Zero for live, one for bulk.</param>
    /// <param name="distanceSquared">Viewer distance squared.</param>
    public void Enqueue(PlayerId player, TerrainBlockKey key, uint version, byte[] immutableBlob, int priorityClass, float distanceSquared)
    {
        ArgumentNullException.ThrowIfNull(immutableBlob);
        if (key.Lod != 0 || immutableBlob.Length > ProtocolConstants.MaxTerrainBlockDataBytes
            || priorityClass is < 0 or > 1 || !float.IsFinite(distanceSquared) || distanceSquared < 0)
        {
            throw new ArgumentException("Invalid terrain queue entry.");
        }
        if (!_players.TryGetValue(player, out var queue))
        {
            queue = new PlayerQueue();
            _players.Add(player, queue);
        }
        if (queue.Entries.TryGetValue(key, out var old) && old.Data.Version > version)
        {
            return;
        }
        var data = new TerrainBlockData(key, version, immutableBlob);
        _writer.Reset();
        MessageCodec.Write(_writer, data);
        // A newer version of a block that is waiting as live stays live, whatever class announced it.
        queue.Entries[key] = new Entry(data, old is null ? priorityClass : Math.Min(old.Priority, priorityClass), distanceSquared, old?.Timestamp ?? queue.Timestamp, _writer.Length);
        if (queue.Entries.Count <= TerrainReplicationPolicy.MaxQueuedBlocks)
        {
            return;
        }
        Entry? farthest = null;
        foreach (var candidate in queue.Entries.Values)
        {
            if (farthest is null || candidate.Distance > farthest.Distance
                || (candidate.Distance == farthest.Distance && candidate.Data.Key > farthest.Data.Key))
            {
                farthest = candidate;
            }
        }
        queue.Entries.Remove(farthest!.Data.Key);
        queue.Resync = true;
    }

    /// <summary>Gets the encoded frame size of the block last returned by <see cref="TryTake(PlayerId, long, out TerrainBlockData?)"/>.</summary>
    public int LastTakenBytes { get; private set; }

    /// <summary>Selects one task and debits its complete encoded frame. No allocation after enqueue.</summary>
    /// <param name="player">Recipient.</param>
    /// <param name="timestamp">Current injected clock timestamp.</param>
    /// <param name="data">Selected data.</param>
    /// <returns>Whether a task fits the shared budget.</returns>
    public bool TryTake(PlayerId player, long timestamp, out TerrainBlockData? data) => TryTake(player, timestamp, liveOnly: false, out data);

    /// <summary>Selects one task, optionally only a live one, and debits its complete encoded frame. No allocation after enqueue.</summary>
    /// <param name="player">Recipient.</param>
    /// <param name="timestamp">Current injected clock timestamp.</param>
    /// <param name="liveOnly">True to leave bulk tasks in the queue.</param>
    /// <param name="data">Selected data.</param>
    /// <returns>Whether a task fits the shared budget.</returns>
    public bool TryTake(PlayerId player, long timestamp, bool liveOnly, out TerrainBlockData? data)
    {
        data = null;
        if (!_players.TryGetValue(player, out var queue))
        {
            return false;
        }
        queue.Timestamp = timestamp;
        Entry? best = null;
        var bulkTurn = !liveOnly && queue.LiveStreak >= LiveTakesPerBulk;
        Entry? bestBulk = null;
        foreach (var candidate in queue.Entries.Values)
        {
            if (liveOnly && candidate.Priority != 0)
            {
                continue;
            }
            if (best is null || Before(candidate, best, timestamp))
            {
                best = candidate;
            }
            if (bulkTurn && candidate.Priority != 0 && (bestBulk is null || Before(candidate, bestBulk, timestamp)))
            {
                bestBulk = candidate;
            }
        }
        best = bestBulk ?? best;
        if (best is null || !_budget.TryConsume(player, best.Priority == 0 ? ReplicationTraffic.Live : ReplicationTraffic.Bulk, best.Bytes))
        {
            return false;
        }
        queue.Entries.Remove(best.Data.Key);
        queue.LiveStreak = best.Priority == 0 ? queue.LiveStreak + 1 : 0;
        data = best.Data;
        return true;
    }

    /// <summary>Whether overflow requires a new interest manifest.</summary>
    /// <param name="player">Recipient.</param>
    /// <returns>Resync flag.</returns>
    public bool NeedsResync(PlayerId player) => _players.TryGetValue(player, out var queue) && queue.Resync;
    /// <summary>Clears the flag after a replacement manifest has been scheduled.</summary>
    /// <param name="player">Recipient.</param>
    public void ResetResync(PlayerId player)
    {
        if (_players.TryGetValue(player, out var queue))
        {
            queue.Resync = false;
        }
    }
    /// <summary>Number of distinct waiting keys.</summary>
    /// <param name="player">Recipient.</param>
    /// <returns>Task count.</returns>
    public int Count(PlayerId player) => _players.TryGetValue(player, out var queue) ? queue.Entries.Count : 0;
    /// <summary>Removes all state of a departed player.</summary>
    /// <param name="player">Departed player.</param>
    public void Remove(PlayerId player) => _players.Remove(player);
    /// <summary>Clears every recipient during shutdown.</summary>
    public void Clear() => _players.Clear();

    private bool Before(Entry candidate, Entry best, long now)
    {
        // Live edits always go before bulk traffic; aging only keeps far blocks of one class from starving behind near ones.
        if (candidate.Priority != best.Priority)
        {
            return candidate.Priority < best.Priority;
        }
        var candidateAged = now - candidate.Timestamp >= _frequency;
        var bestAged = now - best.Timestamp >= _frequency;
        if (candidateAged != bestAged)
        {
            return candidateAged;
        }
        if (candidateAged && candidate.Timestamp != best.Timestamp)
        {
            return candidate.Timestamp < best.Timestamp;
        }
        return candidate.Distance < best.Distance || (candidate.Distance == best.Distance && candidate.Data.Key < best.Data.Key);
    }

    private sealed record Entry(TerrainBlockData Data, int Priority, float Distance, long Timestamp, int Bytes);
    private sealed class PlayerQueue
    {
        internal Dictionary<TerrainBlockKey, Entry> Entries { get; } = [];
        internal long Timestamp { get; set; }
        internal bool Resync { get; set; }
        internal int LiveStreak { get; set; }
    }
}
