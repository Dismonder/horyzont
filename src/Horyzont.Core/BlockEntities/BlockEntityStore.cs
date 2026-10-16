using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;

namespace Horyzont.Core.BlockEntities;

/// <summary>States of the functional blocks of a world, iterated in <see cref="BlockRef"/> order (determinism, ZASADY 4).</summary>
public sealed class BlockEntityStore
{
    private readonly ChangeTracker _tracker;
    private readonly SortedDictionary<BlockRef, BlockEntityState> _states = [];
    private readonly Dictionary<BlockRef, BlockEntityState> _lookup = [];
    private readonly Dictionary<EntityId, int> _perConstruct = [];
    private readonly Dictionary<EntityId, int> _stationsPerConstruct = [];

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

    /// <summary>Number of stations with time (states of kind <see cref="StationBlockState"/>) of one construct (K6-32).</summary>
    /// <param name="construct">The construct.</param>
    /// <returns>The count.</returns>
    public int StationsOf(EntityId construct) => _stationsPerConstruct.GetValueOrDefault(construct);

    /// <summary>Looks up the state of a block.</summary>
    /// <param name="key">Construct and cell.</param>
    /// <param name="state">The state when found.</param>
    /// <returns>True when the block has a state.</returns>
    public bool TryGet(BlockRef key, [NotNullWhen(true)] out BlockEntityState? state)
    {
        // The lookup in the sorted dictionary allocates, so the lookups go through a hash index (read by key only, never iterated; the validators and the
        // station observation ask per intent and per 10 ticks).
        return _lookup.TryGetValue(key, out state);
    }

    /// <summary>Stores or replaces a state and marks it changed; the caller checked the preconditions.</summary>
    /// <returns>The state that was replaced, null when the block had none.</returns>
    internal BlockEntityState? Set(BlockRef key, BlockEntityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var previous = _states.GetValueOrDefault(key);
        _states[key] = state;
        _lookup[key] = state;
        if (previous is null)
        {
            _perConstruct[key.Construct] = _perConstruct.GetValueOrDefault(key.Construct) + 1;
        }

        CountStation(key.Construct, previous, -1);
        CountStation(key.Construct, state, 1);
        _tracker.MarkBlockEntity(key);
        return previous;
    }

    /// <summary>Stores a state read from a checkpoint without marking it changed (world loader); the caller checked the preconditions.</summary>
    internal void Load(BlockRef key, BlockEntityState state)
    {
        _states.Add(key, state);
        _lookup.Add(key, state);
        _perConstruct[key.Construct] = _perConstruct.GetValueOrDefault(key.Construct) + 1;
        CountStation(key.Construct, state, 1);
    }

    /// <summary>Removes a state and marks it changed.</summary>
    /// <returns>The state that was removed, null when the block had none (nothing is marked).</returns>
    internal BlockEntityState? Remove(BlockRef key)
    {
        if (!_states.Remove(key, out var previous))
        {
            return null;
        }

        _lookup.Remove(key);
        var left = _perConstruct[key.Construct] - 1;
        if (left == 0)
        {
            _perConstruct.Remove(key.Construct);
        }
        else
        {
            _perConstruct[key.Construct] = left;
        }

        CountStation(key.Construct, previous, -1);
        _tracker.MarkBlockEntity(key);
        return previous;
    }

    private void CountStation(EntityId construct, BlockEntityState? state, int delta)
    {
        if (state is not StationBlockState)
        {
            return;
        }

        var count = _stationsPerConstruct.GetValueOrDefault(construct) + delta;
        if (count == 0)
        {
            _stationsPerConstruct.Remove(construct);
        }
        else
        {
            _stationsPerConstruct[construct] = count;
        }
    }
}
