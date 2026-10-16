using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;

namespace Horyzont.Core.Props;

/// <summary>The harvested props of a world (K6-31), sorted by planet and prop. K6-31 A2 adds the mutators and the change tracking.</summary>
public sealed class HarvestedPropStore
{
    private readonly List<HarvestedPropRecord> _sorted = [];

    /// <summary>Number of harvested props.</summary>
    public int Count => _sorted.Count;

    /// <summary>All harvested props, sorted by planet and prop.</summary>
    public IReadOnlyList<HarvestedPropRecord> All => _sorted;

    /// <summary>Whether a prop is harvested.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="prop">The prop.</param>
    /// <returns>True when harvested.</returns>
    public bool Contains(PlanetId planet, PropId prop) => TryGetTick(planet, prop, out _);

    /// <summary>Looks up the tick of the harvest of a prop.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="prop">The prop.</param>
    /// <param name="tick">The tick when harvested.</param>
    /// <returns>True when harvested.</returns>
    public bool TryGetTick(PlanetId planet, PropId prop, out Tick tick)
    {
        // K6-31 A2: a binary search over the sorted list.
        foreach (var record in _sorted)
        {
            if (record.Planet == planet && record.Prop == prop)
            {
                tick = record.Tick;
                return true;
            }
        }

        tick = default;
        return false;
    }

    /// <summary>Appends the harvested props of one region in prop order.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="region">The region.</param>
    /// <param name="result">Receives the records; not cleared.</param>
    public void CollectRegion(PlanetId planet, PropRegionKey region, List<HarvestedPropRecord> result)
    {
        // K6-31 A2: a range over the sorted list.
        ArgumentNullException.ThrowIfNull(result);
        _ = (planet, region, _sorted);
    }
}
