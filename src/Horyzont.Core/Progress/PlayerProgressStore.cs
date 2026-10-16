using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Ids;

namespace Horyzont.Core.Progress;

/// <summary>Progress records of the players of a world. K6-30 A2 adds the mutators, the ordering and the change tracking.</summary>
public sealed class PlayerProgressStore
{
    private readonly Dictionary<PlayerId, PlayerProgress> _byPlayer = [];

    /// <summary>Number of records.</summary>
    public int Count => _byPlayer.Count;

    /// <summary>Looks up the record of a player.</summary>
    /// <param name="player">The player.</param>
    /// <param name="progress">The record when found.</param>
    /// <returns>True when the player has a record.</returns>
    public bool TryGet(PlayerId player, [NotNullWhen(true)] out PlayerProgress? progress) => _byPlayer.TryGetValue(player, out progress);
}
