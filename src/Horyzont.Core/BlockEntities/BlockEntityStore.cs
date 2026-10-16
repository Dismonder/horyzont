using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Ids;

namespace Horyzont.Core.BlockEntities;

/// <summary>States of the functional blocks of a world. K6-30 A2 adds the mutators, the ordering and the change tracking.</summary>
public sealed class BlockEntityStore
{
    private readonly Dictionary<BlockRef, BlockEntityState> _states = [];

    /// <summary>Number of states.</summary>
    public int Count => _states.Count;

    /// <summary>Number of states of one construct.</summary>
    /// <param name="construct">The construct.</param>
    /// <returns>The count.</returns>
    public int CountOf(EntityId construct) => _states.Keys.Count(key => key.Construct == construct);

    /// <summary>Looks up the state of a block.</summary>
    /// <param name="key">Construct and cell.</param>
    /// <param name="state">The state when found.</param>
    /// <returns>True when the block has a state.</returns>
    public bool TryGet(BlockRef key, [NotNullWhen(true)] out BlockEntityState? state) => _states.TryGetValue(key, out state);
}
