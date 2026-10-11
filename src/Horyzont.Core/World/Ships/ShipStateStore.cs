using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Ids;

namespace Horyzont.Core.World.Ships;

/// <summary>
/// Flight states of ships by construct id (K5-24 decision 11): outside the journal and outside the state hash. Mutated only by
/// <c>ServerSimulation.UpdateShipState</c>, the applier of <c>ShipFuelAddOp</c>, the world loader and the checkpoint capture.
/// </summary>
public sealed class ShipStateStore
{
    private readonly SortedDictionary<EntityId, ShipStateRecord> _records = new(Comparer<EntityId>.Create(static (a, b) => a.Value.CompareTo(b.Value)));

    /// <summary>Gets the number of records.</summary>
    public int Count => _records.Count;

    /// <summary>Gets the records sorted by construct id.</summary>
    public IReadOnlyCollection<ShipStateRecord> All => _records.Values;

    /// <summary>Looks up the record of a ship.</summary>
    /// <param name="construct">The construct.</param>
    /// <param name="record">Receives the record.</param>
    /// <returns>True when the ship has a record.</returns>
    public bool TryGet(EntityId construct, [MaybeNullWhen(false)] out ShipStateRecord record) => _records.TryGetValue(construct, out record);

    internal void Set(in ShipStateRecord record) => _records[record.Construct] = record;

    internal bool Remove(EntityId construct) => _records.Remove(construct);
}
