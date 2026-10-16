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
    /// <summary>Raised after the map of a construct changed or was cleared.</summary>
    public event Action<EntityId>? Changed;

    /// <summary>Gets the number of block states of a construct.</summary>
    /// <param name="construct">Construct.</param>
    /// <returns>The count, 0 for an unknown construct.</returns>
    public int CountOf(EntityId construct) => throw new NotImplementedException("K6-30 D2");

    /// <summary>Looks up the state of one block.</summary>
    /// <param name="construct">Construct.</param>
    /// <param name="cell">Cell of the block.</param>
    /// <param name="state">The state when the block has one.</param>
    /// <returns>True when found.</returns>
    public bool TryGet(EntityId construct, CellPos cell, [NotNullWhen(true)] out BlockEntityState? state) => throw new NotImplementedException("K6-30 D2");

    /// <summary>Replaces the map of a construct with the list of a message.</summary>
    /// <param name="message">Message 46.</param>
    public void Apply(BlockEntityStates message) => throw new NotImplementedException("K6-30 D2");

    /// <summary>Forgets the states of a construct (a new full state, or the construct is gone).</summary>
    /// <param name="construct">Construct.</param>
    public void Remove(EntityId construct) => throw new NotImplementedException("K6-30 D2");

    /// <summary>Forgets everything (disconnect).</summary>
    public void Clear() => throw new NotImplementedException("K6-30 D2");

    internal void RaiseChanged(EntityId construct) => Changed?.Invoke(construct);
}
