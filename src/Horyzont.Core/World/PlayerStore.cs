using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;

namespace Horyzont.Core.World;

/// <summary>All players known to a world (online and offline). Players are never removed.</summary>
public sealed class PlayerStore
{
    private readonly Dictionary<PlayerId, PlayerState> _byId = [];
    private readonly Dictionary<PlatformIdentity, PlayerId> _byIdentity = [];
    private readonly Dictionary<EntityId, PlayerState> _byEntity = [];
    private readonly List<PlayerState> _sorted = [];
    private readonly ChangeTracker _tracker;

    internal PlayerStore(ChangeTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>Number of players.</summary>
    public int Count => _sorted.Count;

    /// <summary>All players in <see cref="PlayerId"/> order.</summary>
    public IReadOnlyList<PlayerState> All => _sorted;

    /// <summary>Online players in <see cref="PlayerId"/> order.</summary>
    public IEnumerable<PlayerState> Online
    {
        get
        {
            foreach (var player in _sorted)
            {
                if (player.IsOnline)
                {
                    yield return player;
                }
            }
        }
    }

    /// <summary>Looks up a player.</summary>
    /// <param name="id">Player id.</param>
    /// <param name="player">The player when found.</param>
    /// <returns>True when the player exists.</returns>
    public bool TryGet(PlayerId id, [NotNullWhen(true)] out PlayerState? player) => _byId.TryGetValue(id, out player);

    /// <summary>Looks up the player linked to a platform identity.</summary>
    /// <param name="identity">Platform identity.</param>
    /// <param name="player">The player id when found.</param>
    /// <returns>True when the identity is known.</returns>
    public bool TryGetByIdentity(PlatformIdentity identity, out PlayerId player) => _byIdentity.TryGetValue(identity, out player);

    /// <summary>Looks up the player whose body is the given entity.</summary>
    /// <param name="entity">Entity id.</param>
    /// <param name="player">The player when found.</param>
    /// <returns>True when the entity is a player body.</returns>
    public bool TryGetByEntity(EntityId entity, [NotNullWhen(true)] out PlayerState? player) => _byEntity.TryGetValue(entity, out player);

    /// <summary>Adds a new player and marks it changed.</summary>
    internal void Add(PlayerState player)
    {
        Load(player);
        _tracker.MarkPlayer(player.Player);
    }

    /// <summary>Adds a player without marking it changed (world loader).</summary>
    internal void Load(PlayerState player)
    {
        if (_byId.ContainsKey(player.Player) || _byIdentity.ContainsKey(player.Identity) || _byEntity.ContainsKey(player.Entity))
        {
            throw new InvalidDataException($"Duplicate player {player.Player.Value}, its identity or its entity.");
        }

        _byId.Add(player.Player, player);
        _byIdentity.Add(player.Identity, player.Player);
        _byEntity.Add(player.Entity, player);
        var index = _sorted.Count;
        while (index > 0 && _sorted[index - 1].Player.Value > player.Player.Value)
        {
            index--;
        }

        _sorted.Insert(index, player);
    }

    /// <summary>Marks a player changed for the next checkpoint.</summary>
    internal void MarkChanged(PlayerId player) => _tracker.MarkPlayer(player);
}
