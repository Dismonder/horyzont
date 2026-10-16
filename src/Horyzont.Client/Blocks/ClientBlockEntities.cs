using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Blocks;

/// <summary>
/// The states of the functional blocks (chests, beds) of the constructs as the server replicates them (K6-30 D15, message 46).
/// A message holds the whole list of one construct and replaces the map of that construct; a new full state of the construct, its
/// removal and a disconnect clear it. Main thread only.
/// </summary>
public sealed class ClientBlockEntities
{
    private readonly Dictionary<EntityId, Dictionary<CellPos, BlockEntityState>> _constructs = [];

    /// <summary>Raised after the map of a construct changed or was cleared.</summary>
    public event Action<EntityId>? Changed;

    /// <summary>Gets the number of block states of a construct.</summary>
    /// <param name="construct">Construct.</param>
    /// <returns>The count, 0 for an unknown construct.</returns>
    public int CountOf(EntityId construct) => _constructs.TryGetValue(construct, out var map) ? map.Count : 0;

    /// <summary>Looks up the state of one block.</summary>
    /// <param name="construct">Construct.</param>
    /// <param name="cell">Cell of the block.</param>
    /// <param name="state">The state when the block has one.</param>
    /// <returns>True when found.</returns>
    public bool TryGet(EntityId construct, CellPos cell, [NotNullWhen(true)] out BlockEntityState? state)
    {
        if (_constructs.TryGetValue(construct, out var map) && map.TryGetValue(cell, out state))
        {
            return true;
        }

        state = null;
        return false;
    }

    /// <summary>Replaces the map of a construct with the list of a message.</summary>
    /// <param name="message">Message 46.</param>
    public void Apply(BlockEntityStates message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Entries.Count == 0)
        {
            Remove(message.Construct);
            return;
        }

        var map = new Dictionary<CellPos, BlockEntityState>(message.Entries.Count);
        foreach (var entry in message.Entries)
        {
            map[entry.Cell] = entry.State;
        }

        _constructs[message.Construct] = map;
        Changed?.Invoke(message.Construct);
    }

    /// <summary>Forgets the states of a construct (a new full state, or the construct is gone).</summary>
    /// <param name="construct">Construct.</param>
    public void Remove(EntityId construct)
    {
        if (_constructs.Remove(construct))
        {
            Changed?.Invoke(construct);
        }
    }

    /// <summary>Forgets everything (disconnect).</summary>
    public void Clear()
    {
        if (_constructs.Count == 0)
        {
            return;
        }

        var all = new List<EntityId>(_constructs.Keys);
        _constructs.Clear();
        foreach (var construct in all)
        {
            Changed?.Invoke(construct);
        }
    }
}
