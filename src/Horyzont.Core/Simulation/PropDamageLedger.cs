using Horyzont.Core.Ids;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Server memory of the hit points left of damaged props (K6-31 D10). Not world state: lost on restart, never in the journal, the checkpoint or
/// the hash. An entry expires after <see cref="GameRules.PropDamageResetTicks"/> ticks without a hit and at most
/// <see cref="GameRules.MaxDamagedProps"/> entries are kept (the oldest is dropped).
/// </summary>
internal sealed class PropDamageLedger
{
#pragma warning disable CA1822, IDE0060 // K6-31 B5: the skeleton does not read the instance or the arguments yet.
    /// <summary>Creates the ledger.</summary>
    /// <param name="rules">Limits (<see cref="GameRules.PropDamageResetTicks"/>, <see cref="GameRules.MaxDamagedProps"/>).</param>
    internal PropDamageLedger(GameRules rules) => ArgumentNullException.ThrowIfNull(rules);

    /// <summary>Number of entries kept.</summary>
    internal int Count => 0; // K6-31 B5.

    /// <summary>Hit points left of a prop at a tick: the full hit points when it has no entry or the entry expired.</summary>
    internal int Remaining(PlanetId planet, PropId prop, Tick now, int fullHitPoints) => 0; // K6-31 B5.

    /// <summary>Stores the hit points left after a hit at a tick; the oldest entry is dropped when the ledger is full.</summary>
    internal void Record(PlanetId planet, PropId prop, Tick now, int remaining)
    {
        // K6-31 B5.
    }

    /// <summary>Forgets a prop (it was harvested).</summary>
    internal void Remove(PlanetId planet, PropId prop)
    {
        // K6-31 B5.
    }
#pragma warning restore CA1822, IDE0060
}
