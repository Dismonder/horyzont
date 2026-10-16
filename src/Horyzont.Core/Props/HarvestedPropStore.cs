using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;

namespace Horyzont.Core.Props;

/// <summary>
/// The harvested props of a world (K6-31), a sorted list of (planet, prop, tick) searched binarily: the props of one region are a contiguous
/// range because the region is in the high bits of the identifier. Changes are marked for the checkpoint.
/// </summary>
public sealed class HarvestedPropStore
{
    private readonly ChangeTracker _tracker;
    private readonly List<HarvestedPropRecord> _sorted = [];

    internal HarvestedPropStore(ChangeTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>Number of harvested props.</summary>
    public int Count => _sorted.Count;

    /// <summary>All harvested props, sorted by planet and prop.</summary>
    public IReadOnlyList<HarvestedPropRecord> All => _sorted;

    /// <summary>Whether a prop is harvested.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="prop">The prop.</param>
    /// <returns>True when harvested.</returns>
    public bool Contains(PlanetId planet, PropId prop) => FindIndex(planet, prop) >= 0;

    /// <summary>Looks up the tick of the harvest of a prop.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="prop">The prop.</param>
    /// <param name="tick">The tick when harvested.</param>
    /// <returns>True when harvested.</returns>
    public bool TryGetTick(PlanetId planet, PropId prop, out Tick tick)
    {
        var index = FindIndex(planet, prop);
        if (index < 0)
        {
            tick = default;
            return false;
        }

        tick = _sorted[index].Tick;
        return true;
    }

    /// <summary>Appends the harvested props of one region in prop order.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="region">The region.</param>
    /// <param name="result">Receives the records; not cleared.</param>
    public void CollectRegion(PlanetId planet, PropRegionKey region, List<HarvestedPropRecord> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        // The region occupies [region << 24, (region + 1) << 24) of the identifier values.
        var low = region.Value << 24;
        var high = (region.Value + 1) << 24;
        for (var index = LowerBound(planet, low); index < _sorted.Count; index++)
        {
            var record = _sorted[index];
            if (record.Planet != planet || record.Prop.Value >= high)
            {
                break;
            }

            result.Add(record);
        }
    }

    /// <summary>Adds a prop and marks it changed; the caller checked that the prop is not in the set.</summary>
    internal void Add(PlanetId planet, PropId prop, Tick tick)
    {
        var index = LowerBound(planet, prop.Value);
        if (index < _sorted.Count && _sorted[index].Planet == planet && _sorted[index].Prop == prop)
        {
            throw new InvalidOperationException("The prop is already in the harvested set.");
        }

        _sorted.Insert(index, new HarvestedPropRecord(planet, prop, tick));
        _tracker.MarkHarvestedProp(new HarvestedPropKey(planet, prop));
    }

    /// <summary>Adds a prop read from a checkpoint without marking it changed (world loader); the caller checked the preconditions.</summary>
    internal void Load(HarvestedPropRecord record)
    {
        var index = LowerBound(record.Planet, record.Prop.Value);
        if (index < _sorted.Count && _sorted[index].Planet == record.Planet && _sorted[index].Prop == record.Prop)
        {
            throw new InvalidOperationException("The prop is already in the harvested set.");
        }

        _sorted.Insert(index, record);
    }

    /// <summary>Removes a prop and marks it changed.</summary>
    /// <returns>True when the prop was in the set (nothing is marked otherwise); <paramref name="previous"/> then holds its tick.</returns>
    internal bool Remove(PlanetId planet, PropId prop, out Tick previous)
    {
        var index = FindIndex(planet, prop);
        if (index < 0)
        {
            previous = default;
            return false;
        }

        previous = _sorted[index].Tick;
        _sorted.RemoveAt(index);
        _tracker.MarkHarvestedProp(new HarvestedPropKey(planet, prop));
        return true;
    }

    private int FindIndex(PlanetId planet, PropId prop)
    {
        var index = LowerBound(planet, prop.Value);
        return index < _sorted.Count && _sorted[index].Planet == planet && _sorted[index].Prop == prop ? index : -1;
    }

    /// <summary>The first index whose (planet, prop value) is not below the given pair.</summary>
    private int LowerBound(PlanetId planet, ulong propValue)
    {
        var low = 0;
        var high = _sorted.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            var record = _sorted[middle];
            var below = record.Planet.Value < planet.Value || (record.Planet == planet && record.Prop.Value < propValue);
            if (below)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
