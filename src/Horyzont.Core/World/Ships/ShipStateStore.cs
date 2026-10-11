using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Ids;

namespace Horyzont.Core.World.Ships;

/// <summary>
/// Flight states of ships by construct id (K5-24 decision 11): outside the journal and outside the state hash. Mutated only by
/// <c>ServerSimulation.UpdateShipState</c>, the applier of <c>ShipFuelAddOp</c>, the world loader and the checkpoint capture.
/// </summary>
public sealed class ShipStateStore
{
    // The flight reads and writes every tick without allocating (a sorted dictionary boxes the key); the order lives in a list.
    private readonly Dictionary<EntityId, ShipStateRecord> _byId = [];
    private readonly List<EntityId> _order = [];

    /// <summary>Gets the number of records.</summary>
    public int Count => _byId.Count;

    /// <summary>Gets the records sorted by construct id.</summary>
    public IReadOnlyCollection<ShipStateRecord> All => _order.ConvertAll(id => _byId[id]);

    /// <summary>Looks up the record of a ship.</summary>
    /// <param name="construct">The construct.</param>
    /// <param name="record">Receives the record.</param>
    /// <returns>True when the ship has a record.</returns>
    public bool TryGet(EntityId construct, [MaybeNullWhen(false)] out ShipStateRecord record) => _byId.TryGetValue(construct, out record);

    internal void Set(in ShipStateRecord record)
    {
        if (!_byId.ContainsKey(record.Construct))
        {
            _order.Insert(IndexOf(record.Construct.Value), record.Construct);
        }

        _byId[record.Construct] = record;
    }

    internal bool Remove(EntityId construct)
    {
        if (!_byId.Remove(construct))
        {
            return false;
        }

        _order.RemoveAt(IndexOf(construct.Value));
        return true;
    }

    /// <summary>Index of the first id not less than <paramref name="value"/> in the sorted order.</summary>
    private int IndexOf(ulong value)
    {
        int lo = 0, hi = _order.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (_order[mid].Value < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
