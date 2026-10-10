using System.Globalization;
using System.IO.Hashing;
using System.Text;

using Godot;

using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Terrain;

namespace Horyzont.Game.Terrain;

/// <summary>Rebuilds derived blocks from eight current children with the native Voxel Tools edit filter.</summary>
public sealed class DerivedLodRebuilder
{
    private readonly Lock _lock = new();
    private readonly ITerrainBlockSource _source;
    private readonly Func<TerrainBlockKey, byte[]> _generate;
    private readonly TerrainBlockCodec _codec;
    private readonly DerivedLodStore _store;
    private readonly string _profile;
    private readonly byte _maxLod;
    private readonly SortedSet<TerrainBlockKey> _dirty = [];
    private readonly Dictionary<TerrainBlockKey, byte[]> _ready = [];

    /// <summary>Creates a main-thread rebuilder; stream reads only immutable published results.</summary>
    /// <param name="source">Authoritative LOD zero source.</param>
    /// <param name="generate">Main-thread generator for unedited children.</param>
    /// <param name="codec">Canonical envelope codec.</param>
    /// <param name="store">Separate derived database.</param>
    /// <param name="profileFingerprint">Generator profile, parameters and edit semantics.</param>
    /// <param name="maxLod">Highest derived level.</param>
    public DerivedLodRebuilder(ITerrainBlockSource source, Func<TerrainBlockKey, byte[]> generate, TerrainBlockCodec codec,
        DerivedLodStore store, string profileFingerprint, byte maxLod)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(generate);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrEmpty(profileFingerprint);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxLod, (byte)30);
        _source = source;
        _generate = generate;
        _codec = codec;
        _store = store;
        _profile = profileFingerprint;
        _maxLod = maxLod;
    }

    /// <summary>Invalidates every ancestor; shifting signed coordinates implements floor division for negative blocks.</summary>
    /// <param name="lod0Key">New authoritative base.</param>
    public void Invalidate(TerrainBlockKey lod0Key)
    {
        if (lod0Key.Lod != 0)
        {
            throw new ArgumentException("Invalidation requires LOD zero.", nameof(lod0Key));
        }
        lock (_lock)
        {
            for (byte lod = 1; lod <= _maxLod; lod++)
            {
                var ancestor = lod0Key with { Lod = lod, X = lod0Key.X >> lod, Y = lod0Key.Y >> lod, Z = lod0Key.Z >> lod };
                _ready.Remove(ancestor);
                _dirty.Add(ancestor);
            }
        }
    }

    /// <summary>Whether a known changed ancestor must wait instead of returning unedited generator data.</summary>
    /// <param name="key">Derived key.</param>
    /// <returns>Invalidation state.</returns>
    public bool IsDirty(TerrainBlockKey key)
    {
        lock (_lock)
        {
            return _dirty.Contains(key);
        }
    }

    /// <summary>Builds at most two parents per frame, lowest LOD first. Main thread only.</summary>
    /// <param name="maxBlocks">Requested frame work budget.</param>
    public void Step(int maxBlocks)
    {
        if (OS.GetThreadCallerId() != OS.GetMainThreadId())
        {
            throw new InvalidOperationException("Derived terrain rebuilding requires the main thread.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(maxBlocks);
        for (var built = 0; built < Math.Min(2, maxBlocks); built++)
        {
            TerrainBlockKey key;
            lock (_lock)
            {
                if (_dirty.Count == 0)
                {
                    return;
                }
                key = _dirty.Min;
            }
            if (!Build(key, out var fingerprint, out var blob) || !_store.Put(key, fingerprint, blob))
            {
                return;
            }
            lock (_lock)
            {
                if (_ready.Count >= DerivedLodCache.DefaultMaxBlocks)
                {
                    // Eviction is conservative: an evicted changed block stays dirty until rebuilt again.
                    using var enumerator = _ready.Keys.GetEnumerator();
                    enumerator.MoveNext();
                    var evicted = enumerator.Current;
                    _ready.Remove(evicted);
                    _dirty.Add(evicted);
                }
                _ready[key] = blob;
                _dirty.Remove(key);
            }
        }
    }

    /// <summary>Reads an immutable envelope; a known stale parent is never returned.</summary>
    /// <param name="key">Derived block.</param>
    /// <param name="blob">Published envelope.</param>
    /// <returns>Whether its current result is available.</returns>
    public bool TryLoad(TerrainBlockKey key, out byte[] blob)
    {
        lock (_lock)
        {
            if (!_dirty.Contains(key) && _ready.TryGetValue(key, out var ready))
            {
                blob = ready;
                return true;
            }
        }
        blob = [];
        return false;
    }

    private bool Build(TerrainBlockKey parent, out string fingerprint, out byte[] blob)
    {
        fingerprint = string.Empty;
        blob = [];
        using var result = new VoxelBuffer();
        result.Create(16, 16, 16);
        using var childBuffer = new VoxelBuffer();
        var hash = new XxHash64();
        hash.Append(Encoding.UTF8.GetBytes(_profile));
        Span<byte> digestBytes = stackalloc byte[12];
        for (var x = 0; x < 2; x++)
        {
            for (var y = 0; y < 2; y++)
            {
                for (var z = 0; z < 2; z++)
                {
                    var child = parent with { Lod = (byte)(parent.Lod - 1), X = (parent.X * 2) + x, Y = (parent.Y * 2) + y, Z = (parent.Z * 2) + z };
                    byte[] data;
                    uint version = 0;
                    if (child.Lod == 0)
                    {
                        var lookup = _source.TryLoad(child, out version, out data);
                        if (lookup == TerrainBlockLookup.Unavailable)
                        {
                            return false;
                        }
                        if (lookup == TerrainBlockLookup.NotEdited)
                        {
                            data = _generate(child);
                        }
                    }
                    else if (!TryLoad(child, out data))
                    {
                        if (IsDirty(child))
                        {
                            return false;
                        }
                        data = _generate(child);
                    }
                    _codec.Decode(data, childBuffer);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(digestBytes, version);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(digestBytes[4..], _codec.Digest(childBuffer));
                    hash.Append(digestBytes);
                    for (uint channel = 0; channel < (uint)VoxelBuffer.ChannelId.MaxChannels; channel++)
                    {
                        result.SetChannelDepth(channel, childBuffer.GetChannelDepth(channel));
                    }
                    childBuffer.DownscaleTo(result, Vector3I.Zero, new Vector3I(16, 16, 16), new Vector3I(x * 8, y * 8, z * 8));
                }
            }
        }
        fingerprint = hash.GetCurrentHashAsUInt64().ToString("X16", CultureInfo.InvariantCulture);
        if (_store.TryLoad(parent, fingerprint, out blob))
        {
            return true;
        }
        blob = _codec.Encode(result);
        return true;
    }
}
