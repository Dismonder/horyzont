using Horyzont.Core.Checkpoints;

namespace Horyzont.Core.Progress;

/// <summary>Progress keys the world has (K6-32, D10): an ordinal sorted set of 1 to 64 byte keys, at most <c>MaxProgressKeys</c>.</summary>
public sealed class ProgressKeyStore
{
    private readonly ChangeTracker _tracker;
    private readonly SortedSet<string> _keys = new(StringComparer.Ordinal);

    internal ProgressKeyStore(ChangeTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>Gets the number of keys.</summary>
    public int Count => _keys.Count;

    /// <summary>Gets the keys in ordinal order.</summary>
    public IReadOnlyCollection<string> Sorted => _keys;

    /// <summary>Whether the world has the key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>True when granted.</returns>
    public bool Contains(string key) => _keys.Contains(key);

    /// <summary>Adds a key and marks the set changed; false when it was there.</summary>
    /// <param name="key">The key.</param>
    /// <returns>True when the key was added.</returns>
    internal bool Add(string key)
    {
        if (!_keys.Add(key))
        {
            return false;
        }

        _tracker.MarkProgressKeys();
        return true;
    }

    /// <summary>Removes a key and marks the set changed (undo of a grant).</summary>
    /// <param name="key">The key.</param>
    /// <returns>True when the key was removed.</returns>
    internal bool Remove(string key)
    {
        if (!_keys.Remove(key))
        {
            return false;
        }

        _tracker.MarkProgressKeys();
        return true;
    }

    /// <summary>Stores a key read from a checkpoint without marking it changed (world loader).</summary>
    /// <param name="key">The key.</param>
    internal void Load(string key) => _keys.Add(key);
}
