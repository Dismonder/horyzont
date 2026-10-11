using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;

namespace Horyzont.Core.World.Markers;

/// <summary>All map markers of a world (K5-27), iterated in id order. Mutated only by the applier and the builder.</summary>
public sealed class MarkerStore
{
    private readonly SortedDictionary<ulong, MarkerRecord> _byId = [];
    private readonly Dictionary<PlayerId, int> _privateCounts = [];
    private readonly ChangeTracker _tracker;
    private int _sharedCount;

    internal MarkerStore(ChangeTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>Number of markers.</summary>
    public int Count => _byId.Count;

    /// <summary>Number of shared markers.</summary>
    public int CountShared => _sharedCount;

    /// <summary>All markers in id order.</summary>
    public IReadOnlyCollection<MarkerRecord> All => _byId.Values;

    /// <summary>Looks up a marker.</summary>
    /// <param name="id">Marker id.</param>
    /// <param name="marker">The marker when found.</param>
    /// <returns>True when the marker exists.</returns>
    public bool TryGet(MarkerId id, [NotNullWhen(true)] out MarkerRecord? marker) => _byId.TryGetValue(id.Value, out marker);

    /// <summary>Number of private markers of a player.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The count.</returns>
    public int CountPrivate(PlayerId player) => _privateCounts.GetValueOrDefault(player);

    /// <summary>Inserts or replaces a marker and marks it changed.</summary>
    internal void Upsert(MarkerRecord marker)
    {
        Load(marker);
        _tracker.MarkMarker(marker.Id);
    }

    /// <summary>Removes a marker and marks it changed.</summary>
    internal void Remove(MarkerId id)
    {
        if (_byId.Remove(id.Value, out var old))
        {
            Uncount(old);
            _tracker.MarkMarker(id);
        }
    }

    /// <summary>Inserts or replaces a marker without marking it changed (loading).</summary>
    internal void Load(MarkerRecord marker)
    {
        if (_byId.TryGetValue(marker.Id.Value, out var old))
        {
            Uncount(old);
        }

        _byId[marker.Id.Value] = marker;
        if (marker.Owner is { } owner)
        {
            _privateCounts[owner] = _privateCounts.GetValueOrDefault(owner) + 1;
        }
        else
        {
            _sharedCount++;
        }
    }

    private void Uncount(MarkerRecord old)
    {
        if (old.Owner is { } owner)
        {
            var left = _privateCounts[owner] - 1;
            if (left == 0)
            {
                _privateCounts.Remove(owner);
            }
            else
            {
                _privateCounts[owner] = left;
            }
        }
        else
        {
            _sharedCount--;
        }
    }
}
