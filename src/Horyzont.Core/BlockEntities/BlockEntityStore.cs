using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;

namespace Horyzont.Core.BlockEntities;

/// <summary>States of the functional blocks of a world, iterated in <see cref="BlockRef"/> order (determinism, ZASADY 4).</summary>
public sealed class BlockEntityStore
{
    private readonly ChangeTracker _tracker;
    private readonly SortedDictionary<BlockRef, BlockEntityState> _states = [];
    private readonly Dictionary<EntityId, int> _perConstruct = [];

    internal BlockEntityStore(ChangeTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>Number of states.</summary>
    public int Count => _states.Count;

    /// <summary>All states sorted by construct, then by cell.</summary>
    public IReadOnlyDictionary<BlockRef, BlockEntityState> All => _states;

    /// <summary>The sorted states for the state hash: the concrete type gives a struct enumerator (no allocation).</summary>
    internal SortedDictionary<BlockRef, BlockEntityState> Sorted => _states;

    /// <summary>Number of states of one construct.</summary>
    /// <param name="construct">The construct.</param>
    /// <returns>The count.</returns>
    public int CountOf(EntityId construct) => _perConstruct.GetValueOrDefault(construct);

    /// <summary>Looks up the state of a block.</summary>
    /// <param name="key">Construct and cell.</param>
    /// <param name="state">The state when found.</param>
    /// <returns>True when the block has a state.</returns>
    public bool TryGet(BlockRef key, [NotNullWhen(true)] out BlockEntityState? state) => _states.TryGetValue(key, out state);

    /// <summary>Stores or replaces a state and marks it changed; the caller checked the preconditions.</summary>
    /// <returns>The state that was replaced, null when the block had none.</returns>
    internal BlockEntityState? Set(BlockRef key, BlockEntityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var previous = _states.GetValueOrDefault(key);
        _states[key] = state;
        if (previous is null)
        {
            _perConstruct[key.Construct] = _perConstruct.GetValueOrDefault(key.Construct) + 1;
        }

        _tracker.MarkBlockEntity(key);
        return previous;
    }

    /// <summary>Stores a state read from a checkpoint without marking it changed (world loader); the caller checked the preconditions.</summary>
    internal void Load(BlockRef key, BlockEntityState state)
    {
        _states.Add(key, state);
        _perConstruct[key.Construct] = _perConstruct.GetValueOrDefault(key.Construct) + 1;
    }

    /// <summary>Removes a state and marks it changed.</summary>
    /// <returns>The state that was removed, null when the block had none (nothing is marked).</returns>
    internal BlockEntityState? Remove(BlockRef key)
    {
        if (!_states.Remove(key, out var previous))
        {
            return null;
        }

        var left = _perConstruct[key.Construct] - 1;
        if (left == 0)
        {
            _perConstruct.Remove(key.Construct);
        }
        else
        {
            _perConstruct[key.Construct] = left;
        }

        _tracker.MarkBlockEntity(key);
        return previous;
    }
}
