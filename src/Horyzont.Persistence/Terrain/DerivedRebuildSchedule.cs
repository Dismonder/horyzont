using Horyzont.Core.World.Terrain;

namespace Horyzont.Persistence.Terrain;

/// <summary>
/// Bookkeeping of the derived LOD rebuild: which ancestors changed (dirty), which results are published (ready), and in which order
/// dirty blocks are attempted. Attempts rotate through the dirty set, so blocks whose children are not ready cannot starve the
/// others, and a full ready set evicts the oldest result (never a result that was just published or re-published). Not thread-safe:
/// the owner locks.
/// </summary>
public sealed class DerivedRebuildSchedule
{
    private readonly int _capacity;
    private readonly SortedSet<TerrainBlockKey> _dirty = [];
    private readonly Dictionary<TerrainBlockKey, (byte[] Blob, long Sequence)> _ready = [];
    private readonly Queue<(TerrainBlockKey Key, long Sequence)> _order = new();
    private long _sequence;
    private TerrainBlockKey? _cursor;
    private bool _missed;

    /// <summary>Creates an empty queue.</summary>
    /// <param name="capacity">Most published results kept.</param>
    public DerivedRebuildSchedule(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>Number of changed blocks waiting for a rebuild.</summary>
    public int DirtyCount => _dirty.Count;

    /// <summary>Number of published results.</summary>
    public int ReadyCount => _ready.Count;

    /// <summary>Marks a block as changed: its published result is dropped and it waits for a rebuild.</summary>
    /// <param name="key">Derived block.</param>
    public void MarkDirty(TerrainBlockKey key)
    {
        _ready.Remove(key);
        _dirty.Add(key);
    }

    /// <summary>Whether the block waits for a rebuild.</summary>
    /// <param name="key">Derived block.</param>
    /// <returns>True when dirty.</returns>
    public bool IsDirty(TerrainBlockKey key) => _dirty.Contains(key);

    /// <summary>Whether the block is dirty or has a published result.</summary>
    /// <param name="key">Derived block.</param>
    /// <returns>True when tracked.</returns>
    public bool Tracks(TerrainBlockKey key) => _dirty.Contains(key) || _ready.ContainsKey(key);

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
    /// has to load the area again once the rebuild is done (<see cref="TakeReloadDue"/>).
    /// </summary>
    public void NoteMissed() => _missed = true;

    /// <summary>True once, when a request was turned away and nothing is dirty any more: the engine must reload now.</summary>
    /// <returns>Whether a reload is due.</returns>
    public bool TakeReloadDue()
    {
        if (!_missed || _dirty.Count != 0)
        {
            return false;
        }

        _missed = false;
        return true;
    }

    /// <summary>Moves the rotating cursor past a block whose rebuild was attempted (built or not).</summary>
    /// <param name="key">The attempted block.</param>
    public void Attempted(TerrainBlockKey key) => _cursor = key;

    /// <summary>Publishes the result of a rebuild; at capacity the oldest other result goes back to dirty.</summary>
    /// <param name="key">Rebuilt block.</param>
    /// <param name="blob">Its envelope.</param>
    public void Publish(TerrainBlockKey key, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        _dirty.Remove(key);
        _ready.Remove(key);
        while (_ready.Count >= _capacity && _order.TryDequeue(out var oldest))
        {
            if (_ready.TryGetValue(oldest.Key, out var held) && held.Sequence == oldest.Sequence)
            {
                // Conservative: an evicted changed block stays dirty until it is rebuilt again.
                _ready.Remove(oldest.Key);
                _dirty.Add(oldest.Key);
            }
        }

        var sequence = ++_sequence;
        _ready[key] = (blob, sequence);
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
