using Horyzont.Core.World.Terrain;

namespace Horyzont.Game.Terrain;

/// <summary>
/// Bounded in-memory store of terrain blocks with LOD &gt; 0 that Voxel Tools asked to save (mipmaps derived from LOD 0, so only
/// visual): served back when the same block loads again in this session. The oldest entries are dropped beyond
/// <see cref="MaxBlocks"/>; a dropped block comes from the generator until Voxel Tools derives it again. Thread-safe (called from
/// Voxel Tools worker threads).
/// </summary>
public sealed class DerivedLodCache
{
    /// <summary>Default capacity.</summary>
    public const int DefaultMaxBlocks = 8192;

    private readonly Lock _lock = new();
    private readonly Dictionary<TerrainBlockKey, Entry> _entries = [];
    private readonly Queue<(TerrainBlockKey Key, long Sequence)> _order = new();
    private long _sequence;

    /// <summary>Creates the cache.</summary>
    /// <param name="maxBlocks">Capacity in blocks.</param>
    public DerivedLodCache(int maxBlocks = DefaultMaxBlocks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBlocks, 1);
        MaxBlocks = maxBlocks;
    }

    /// <summary>Capacity in blocks.</summary>
    public int MaxBlocks { get; }

    /// <summary>Number of blocks held.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Stores or replaces a block (serialized voxels).</summary>
    /// <param name="key">A key with LOD &gt; 0.</param>
    /// <param name="voxels">Serialized voxels; not changed afterwards.</param>
    /// <exception cref="ArgumentException">LOD 0 (those blocks have versions and live in the terrain buffer).</exception>
    public void Put(TerrainBlockKey key, byte[] voxels)
    {
        ArgumentNullException.ThrowIfNull(voxels);
        if (key.Lod == 0)
        {
            throw new ArgumentException("LOD 0 blocks are not derived data.", nameof(key));
        }

        lock (_lock)
        {
            var sequence = ++_sequence;
            _entries[key] = new Entry(voxels, sequence);
            _order.Enqueue((key, sequence));
            while (_entries.Count > MaxBlocks && _order.TryDequeue(out var oldest))
            {
                if (_entries.TryGetValue(oldest.Key, out var entry) && entry.Sequence == oldest.Sequence)
                {
                    _entries.Remove(oldest.Key);
                }
            }

            // Replaced entries leave stale queue items; drop them once they outnumber the live ones.
            if (_order.Count > 2 * MaxBlocks)
            {
                var live = _order.Count;
                for (var i = 0; i < live; i++)
                {
                    var item = _order.Dequeue();
                    if (_entries.TryGetValue(item.Key, out var entry) && entry.Sequence == item.Sequence)
                    {
                        _order.Enqueue(item);
                    }
                }
            }
        }
    }

    /// <summary>Gets a stored block.</summary>
    /// <param name="key">The key.</param>
    /// <param name="voxels">Serialized voxels when found.</param>
    /// <returns>Whether the block is stored.</returns>
    public bool TryGet(TerrainBlockKey key, out byte[] voxels)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                voxels = entry.Voxels;
                return true;
            }
        }

        voxels = [];
        return false;
    }

    private readonly record struct Entry(byte[] Voxels, long Sequence);
}
