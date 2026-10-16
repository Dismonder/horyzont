using Horyzont.Core.Ids;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Server memory of the hit points left of damaged props (K6-31 D10). Not world state: lost on restart, never in the journal, the checkpoint or
/// the hash. An entry expires after <see cref="GameRules.PropDamageResetTicks"/> ticks without a hit and at most
/// <see cref="GameRules.MaxDamagedProps"/> entries are kept (the one hit longest ago is dropped). Main thread only.
/// </summary>
internal sealed class PropDamageLedger
{
    private readonly int _resetTicks;
    private readonly int _capacity;
    private readonly Dictionary<(PlanetId Planet, PropId Prop), LinkedListNode<Entry>> _entries;
    private readonly LinkedList<Entry> _byLastHit = new();

    /// <summary>Creates the ledger.</summary>
    /// <param name="rules">Limits (<see cref="GameRules.PropDamageResetTicks"/>, <see cref="GameRules.MaxDamagedProps"/>).</param>
    internal PropDamageLedger(GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _resetTicks = rules.PropDamageResetTicks;
        _capacity = Math.Max(1, rules.MaxDamagedProps);
        _entries = new Dictionary<(PlanetId, PropId), LinkedListNode<Entry>>();
    }

    /// <summary>Number of entries kept (expired ones stay until they are dropped or overwritten).</summary>
    internal int Count => _entries.Count;

    /// <summary>Hit points left of a prop at a tick: the full hit points when it has no entry or the entry expired.</summary>
    internal int Remaining(PlanetId planet, PropId prop, Tick now, int fullHitPoints)
    {
        if (!_entries.TryGetValue((planet, prop), out var node))
        {
            return fullHitPoints;
        }

        var entry = node.Value;
        return now.Value >= entry.LastHit.Value && now.Value - entry.LastHit.Value >= (ulong)_resetTicks ? fullHitPoints : entry.Remaining;
    }

    /// <summary>Stores the hit points left after a hit at a tick; the entry hit longest ago is dropped when the ledger is full.</summary>
    internal void Record(PlanetId planet, PropId prop, Tick now, int remaining)
    {
        var key = (planet, prop);
        if (_entries.TryGetValue(key, out var node))
        {
            node.Value = new Entry(key, remaining, now);
            _byLastHit.Remove(node);
            _byLastHit.AddLast(node);
            return;
        }

        if (_entries.Count >= _capacity && _byLastHit.First is { } oldest)
        {
            _entries.Remove(oldest.Value.Key);
            _byLastHit.RemoveFirst();
        }

        _entries[key] = _byLastHit.AddLast(new Entry(key, remaining, now));
    }

    /// <summary>Forgets a prop (it was harvested).</summary>
    internal void Remove(PlanetId planet, PropId prop)
    {
        if (_entries.Remove((planet, prop), out var node))
        {
            _byLastHit.Remove(node);
        }
    }

    private readonly record struct Entry((PlanetId Planet, PropId Prop) Key, int Remaining, Tick LastHit);
}
