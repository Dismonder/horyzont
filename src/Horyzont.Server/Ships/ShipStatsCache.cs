using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Ships;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Server.Ships;

/// <summary>
/// <see cref="ShipStats"/> of the constructs in <see cref="ConstructMode.Ship"/>. Recomputed when a construct enters the ship
/// mode and on every block change of a ship, dropped when it leaves the mode or disappears. Entries carry the construct version
/// they were computed for, so a read after a journal replay (which raises no events) recomputes instead of answering stale.
/// Main thread only.
/// </summary>
public sealed class ShipStatsCache
{
    private readonly ServerSimulation _simulation;
    private readonly Dictionary<EntityId, Entry> _entries = [];

    /// <summary>Initializes a new instance of the <see cref="ShipStatsCache"/> class and subscribes to the applied operations.</summary>
    /// <param name="simulation">The simulation whose state and block content are read.</param>
    public ShipStatsCache(ServerSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        _simulation = simulation;
        simulation.Applied += OnApplied;
    }

    /// <summary>Number of cached ships.</summary>
    public int Count => _entries.Count;

    /// <summary>Stats of a construct in the ship mode.</summary>
    /// <param name="construct">Construct id.</param>
    /// <param name="stats">The stats when the construct is a ship.</param>
    /// <returns>False when the construct does not exist or is not in the ship mode.</returns>
    public bool TryGet(EntityId construct, [NotNullWhen(true)] out ShipStats? stats)
    {
        stats = null;
        if (!_simulation.State.Constructs.TryGet(construct, out var current) || current.Mode != ConstructMode.Ship)
        {
            _entries.Remove(construct);
            return false;
        }

        stats = Refresh(current);
        return true;
    }

    private void OnApplied(AppliedOperation applied)
    {
        foreach (var change in applied.ConstructChanges)
        {
            if (!_simulation.State.Constructs.TryGet(change.Construct, out var construct) || construct.Mode != ConstructMode.Ship)
            {
                _entries.Remove(change.Construct);
                continue;
            }

            Refresh(construct);
        }
    }

    private ShipStats Refresh(Construct construct)
    {
        if (_entries.TryGetValue(construct.Id, out var entry) && entry.Version == construct.Version)
        {
            return entry.Stats;
        }

        var stats = ShipStatsCalculator.Compute(construct, _simulation.Content);
        _entries[construct.Id] = new Entry(construct.Version, stats);
        return stats;
    }

    private readonly record struct Entry(uint Version, ShipStats Stats);
}
