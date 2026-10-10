using Horyzont.Core.World.Terrain;

namespace Horyzont.Persistence.Terrain;

/// <summary>
/// Bookkeeping of the derived LOD rebuild: which ancestors changed (dirty), which results are published (ready), and in which order
/// dirty blocks are attempted. Attempts rotate through the dirty set, so blocks whose children are not ready cannot starve the
/// others, and a full ready set evicts the blob of the oldest result (never a result that was just published or re-published); the
/// evicted block stays current and remembers the fingerprint it was stored with, so the owner can read it back from the derived store
/// instead of rebuilding it (otherwise more changed blocks than the capacity would never settle). Not thread-safe: the owner locks.
/// </summary>
public sealed class DerivedRebuildSchedule
{
    /// <summary>Most turned-away blocks remembered; beyond it only the fact that something was missed is kept.</summary>
    public const int MaxMissedKeys = 4096;

    /// <summary>Shortest time between two engine reloads requested by the schedule.</summary>
    public static readonly TimeSpan MinReloadInterval = TimeSpan.FromSeconds(2);

    private readonly int _capacity;
    private readonly SortedSet<TerrainBlockKey> _dirty = [];
    private readonly Dictionary<TerrainBlockKey, string> _evicted = [];
    private readonly HashSet<TerrainBlockKey> _missedKeys = [];
    private readonly List<TerrainBlockKey> _settled = [];
    private readonly Dictionary<TerrainBlockKey, (byte[] Blob, long Sequence, string? Fingerprint)> _ready = [];
    private readonly Queue<(TerrainBlockKey Key, long Sequence)> _order = new();
    private long _sequence;
    private TerrainBlockKey? _cursor;
    private bool _missedOverflow;
    private TimeSpan? _lastReload;

    /// <summary>Creates an empty queue.</summary>
    /// <param name="capacity">Most published results kept.</param>
    public DerivedRebuildSchedule(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>Number of changed blocks waiting for a rebuild.</summary>
    public int DirtyCount => _dirty.Count;

    /// <summary>Number of published results whose blob is held.</summary>
    public int ReadyCount => _ready.Count;

    /// <summary>Number of current results whose blob was evicted and has to be read back from the store.</summary>
    public int EvictedCount => _evicted.Count;

    /// <summary>Marks a block as changed: its published result is dropped and it waits for a rebuild.</summary>
    /// <param name="key">Derived block.</param>
    public void MarkDirty(TerrainBlockKey key)
    {
        _ready.Remove(key);
        _evicted.Remove(key);
        _dirty.Add(key);
    }

    /// <summary>The fingerprint of a current result whose blob was evicted from memory.</summary>
    /// <param name="key">Derived block.</param>
    /// <param name="fingerprint">The fingerprint the result was stored with.</param>
    /// <returns>Whether the block is current but evicted.</returns>
    public bool TryGetEvicted(TerrainBlockKey key, out string fingerprint)
    {
        if (!_dirty.Contains(key) && _evicted.TryGetValue(key, out var found))
        {
            fingerprint = found;
            return true;
        }

        fingerprint = string.Empty;
        return false;
    }

    /// <summary>Whether the block waits for a rebuild.</summary>
    /// <param name="key">Derived block.</param>
    /// <returns>True when dirty.</returns>
    public bool IsDirty(TerrainBlockKey key) => _dirty.Contains(key);

    /// <summary>Whether the block is dirty or has a published result.</summary>
    /// <param name="key">Derived block.</param>
    /// <returns>True when tracked.</returns>
    public bool Tracks(TerrainBlockKey key) => _dirty.Contains(key) || _ready.ContainsKey(key) || _evicted.ContainsKey(key);

    /// <summary>Reads the published result of a block that is not dirty.</summary>
    /// <param name="key">Derived block.</param>
    /// <param name="blob">The result when available.</param>
    /// <returns>Whether a current result exists.</returns>
    public bool TryGetReady(TerrainBlockKey key, out byte[] blob)
    {
        if (!_dirty.Contains(key) && _ready.TryGetValue(key, out var ready))
        {
            blob = ready.Blob;
            return true;
        }

        blob = [];
        return false;
    }

    /// <summary>
    /// Lists up to <paramref name="max"/> dirty blocks to attempt: lowest levels first from the rotating cursor (the block after the
    /// last one reported by <see cref="Attempted"/>), wrapping around, so a block that cannot be built yet does not hold back the rest.
    /// </summary>
    /// <param name="into">Receives the keys; cleared first.</param>
    /// <param name="max">Most keys.</param>
    public void NextAttempts(List<TerrainBlockKey> into, int max)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        if (_dirty.Count == 0 || max <= 0)
        {
            return;
        }

        if (_cursor is { } cursor)
        {
            foreach (var key in _dirty.GetViewBetween(cursor, _dirty.Max))
            {
                if (key.CompareTo(cursor) > 0)
                {
                    into.Add(key);
                    if (into.Count >= max)
                    {
                        return;
                    }
                }
            }

            foreach (var key in _dirty.GetViewBetween(_dirty.Min, cursor))
            {
                into.Add(key);
                if (into.Count >= max)
                {
                    return;
                }
            }

            return;
        }

        foreach (var key in _dirty)
        {
            into.Add(key);
            if (into.Count >= max)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Records that the engine asked for a dirty block and was turned away, so it holds a placeholder instead of the rebuilt data and
    /// has to load it again once that block is rebuilt (<see cref="TakeReloadDue"/>).
    /// </summary>
    /// <param name="key">The block the engine asked for.</param>
    public void NoteMissed(TerrainBlockKey key)
    {
        if (_missedKeys.Count < MaxMissedKeys || _missedKeys.Contains(key))
        {
            _missedKeys.Add(key);
        }
        else
        {
            _missedOverflow = true;
        }
    }

    /// <summary>
    /// True when a turned-away block is rebuilt by now and the last reload is at least <see cref="MinReloadInterval"/> ago: the engine
    /// must load again. It does not wait for the other dirty blocks, so continuing edits cannot hold a reload back; blocks still dirty
    /// stay remembered and are reported once they are rebuilt. Voxel Tools has no per-area reload for a block stream, so the owner
    /// reloads the whole terrain, and the interval is what bounds its cost.
    /// </summary>
    /// <param name="now">A monotonic clock reading.</param>
    /// <returns>Whether a reload is due now.</returns>
    public bool TakeReloadDue(TimeSpan now)
    {
        if (_missedKeys.Count == 0 && !_missedOverflow)
        {
            return false;
        }

        if (_lastReload is { } last && now - last < MinReloadInterval)
        {
            return false;
        }

        _settled.Clear();
        foreach (var key in _missedKeys)
        {
            if (!_dirty.Contains(key))
            {
                _settled.Add(key);
            }
        }

        if (_settled.Count == 0 && (!_missedOverflow || _dirty.Count != 0))
        {
            return false;
        }

        foreach (var key in _settled)
        {
            _missedKeys.Remove(key);
        }

        // Blocks beyond the memory bound are not known by key: the fact stays until nothing is dirty, so none is forgotten unbuilt.
        _missedOverflow = _missedOverflow && _dirty.Count != 0;
        _lastReload = now;
        return true;
    }

    /// <summary>Moves the rotating cursor past a block whose rebuild was attempted (built or not).</summary>
    /// <param name="key">The attempted block.</param>
    public void Attempted(TerrainBlockKey key) => _cursor = key;

    /// <summary>Publishes the result of a rebuild; at capacity the blob of the oldest other result is dropped.</summary>
    /// <param name="key">Rebuilt block.</param>
    /// <param name="blob">Its envelope.</param>
    /// <param name="fingerprint">
    /// What the result was stored with in the derived store. An evicted result with a fingerprint stays current and is read back from
    /// the store; without one it cannot be restored and goes back to dirty.
    /// </param>
    public void Publish(TerrainBlockKey key, byte[] blob, string? fingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(blob);
        _dirty.Remove(key);
        _ready.Remove(key);
        _evicted.Remove(key);
        while (_ready.Count >= _capacity && _order.TryDequeue(out var oldest))
        {
            if (_ready.TryGetValue(oldest.Key, out var held) && held.Sequence == oldest.Sequence)
            {
                _ready.Remove(oldest.Key);
                if (held.Fingerprint is { } stored)
                {
                    _evicted[oldest.Key] = stored;
                }
                else
                {
                    _dirty.Add(oldest.Key);
                }
            }
        }

        var sequence = ++_sequence;
        _ready[key] = (blob, sequence, fingerprint);
        _order.Enqueue((key, sequence));
        if (_order.Count > (_capacity * 2) + 16)
        {
            // Entries of blocks that went dirty meanwhile are stale; drop them so the order list stays bounded.
            var live = _ready.OrderBy(static pair => pair.Value.Sequence).Select(static pair => (pair.Key, pair.Value.Sequence)).ToList();
            _order.Clear();
            foreach (var entry in live)
            {
                _order.Enqueue(entry);
            }
        }
    }
}
