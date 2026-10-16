using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;

namespace Horyzont.Core.Progress;

/// <summary>Progress records of the players of a world, iterated by player id. A record exists only for a player with a respawn point (D11).</summary>
public sealed class PlayerProgressStore
{
    private static readonly Comparer<PlayerId> _byValue = Comparer<PlayerId>.Create(static (a, b) => a.Value.CompareTo(b.Value));

    private readonly ChangeTracker _tracker;
    private readonly SortedDictionary<PlayerId, PlayerProgress> _byPlayer = new(_byValue);

    internal PlayerProgressStore(ChangeTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>Number of records.</summary>
    public int Count => _byPlayer.Count;

    /// <summary>All records sorted by player id.</summary>
    public IReadOnlyDictionary<PlayerId, PlayerProgress> All => _byPlayer;

    /// <summary>Looks up the record of a player.</summary>
    /// <param name="player">The player.</param>
    /// <param name="progress">The record when found.</param>
    /// <returns>True when the player has a record.</returns>
    public bool TryGet(PlayerId player, [NotNullWhen(true)] out PlayerProgress? progress) => _byPlayer.TryGetValue(player, out progress);

    /// <summary>Stores or replaces a record and marks it changed; the caller checked the preconditions.</summary>
    /// <returns>The record that was replaced, null when the player had none.</returns>
    internal PlayerProgress? Set(PlayerProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var previous = _byPlayer.GetValueOrDefault(progress.Player);
        _byPlayer[progress.Player] = progress;
        _tracker.MarkProgress(progress.Player);
        return previous;
    }

    /// <summary>Stores a record read from a checkpoint without marking it changed (world loader); the caller checked the preconditions.</summary>
    internal void Load(PlayerProgress progress) => _byPlayer.Add(progress.Player, progress);

    /// <summary>Removes the record of a player and marks it changed.</summary>
    /// <returns>The record that was removed, null when the player had none (nothing is marked).</returns>
    internal PlayerProgress? Remove(PlayerId player)
    {
        if (!_byPlayer.Remove(player, out var previous))
        {
            return null;
        }

        _tracker.MarkProgress(player);
        return previous;
    }
}
