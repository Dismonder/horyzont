using Horyzont.Core.Checkpoints;

namespace Horyzont.Core.World.Terrain;

/// <summary>Versions of edited terrain blocks.</summary>
public sealed class TerrainVersionTable
{
    private readonly SortedDictionary<TerrainBlockKey, uint> _versions = [];
    private readonly ChangeTracker _tracker;

    internal TerrainVersionTable(ChangeTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>Number of edited blocks.</summary>
    public int Count => _versions.Count;

    /// <summary>All versions sorted by key.</summary>
    public IEnumerable<TerrainBlockVersion> All
    {
        get
        {
            foreach (var pair in _versions)
            {
                yield return new TerrainBlockVersion(pair.Key, pair.Value);
            }
        }
    }

    /// <summary>Version of a block.</summary>
    /// <param name="key">Block key.</param>
    /// <returns>The version; 0 when never edited.</returns>
    public uint Get(TerrainBlockKey key) => _versions.TryGetValue(key, out var version) ? version : 0;

    /// <summary>Increments the version of a block, marks it for the checkpoint and returns the new version.</summary>
    internal uint Bump(TerrainBlockKey key)
    {
        var version = Get(key) + 1;
        _versions[key] = version;
        _tracker.MarkTerrain(key);
        return version;
    }

    /// <summary>Sets a loaded version without marking it (world loader).</summary>
    internal void Load(TerrainBlockVersion version)
    {
        if (version.Version == 0 || !_versions.TryAdd(version.Key, version.Version))
        {
            throw new InvalidDataException($"Invalid or duplicate terrain version {version.Key}.");
        }
    }
}
