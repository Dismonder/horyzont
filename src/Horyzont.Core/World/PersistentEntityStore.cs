using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;

namespace Horyzont.Core.World;

/// <summary>The persistent entities of a world sorted by id, with an index by owner (K6-35 D20).</summary>
public sealed class PersistentEntityStore
{
    private static readonly List<PersistentEntity> _none = [];
    private readonly ChangeTracker _tracker;
    private readonly Dictionary<EntityId, PersistentEntity> _byId = [];
    private readonly List<PersistentEntity> _sorted = [];
    private readonly Dictionary<PlayerId, List<PersistentEntity>> _byOwner = [];

    internal PersistentEntityStore(ChangeTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>Gets the number of entities.</summary>
    public int Count => _sorted.Count;

    /// <summary>Gets all entities sorted by id.</summary>
    public IReadOnlyList<PersistentEntity> All => _sorted;

    /// <summary>Looks up an entity.</summary>
    /// <param name="id">Entity id.</param>
    /// <param name="entity">The entity when found.</param>
    /// <returns>True when it exists.</returns>
    public bool TryGet(EntityId id, [NotNullWhen(true)] out PersistentEntity? entity) => _byId.TryGetValue(id, out entity);

    /// <summary>The entities of one owner sorted by id, kept up to date incrementally.</summary>
    /// <param name="owner">The owner.</param>
    /// <returns>The entities; empty when the owner has none.</returns>
    public IReadOnlyList<PersistentEntity> OwnedBy(PlayerId owner) => _byOwner.TryGetValue(owner, out var list) ? list : _none;

    /// <summary>Adds an entity and marks it changed.</summary>
    internal void Add(PersistentEntity entity)
    {
        Load(entity);
        _tracker.MarkPersistentEntity(entity.Id);
    }

    /// <summary>Adds an entity without marking it changed (world loader).</summary>
    internal void Load(PersistentEntity entity)
    {
        if (!_byId.TryAdd(entity.Id, entity))
        {
            throw new InvalidDataException($"Duplicate persistent entity {entity.Id.Value}.");
        }

        Insert(_sorted, entity);
        if (!_byOwner.TryGetValue(entity.Owner, out var owned))
        {
            owned = [];
            _byOwner.Add(entity.Owner, owned);
        }

        Insert(owned, entity);
    }

    /// <summary>Removes an entity and marks it changed.</summary>
    internal bool Remove(EntityId id)
    {
        if (!_byId.Remove(id, out var entity))
        {
            return false;
        }

        _sorted.Remove(entity);
        var owned = _byOwner[entity.Owner];
        owned.Remove(entity);
        if (owned.Count == 0)
        {
            _byOwner.Remove(entity.Owner);
        }

        _tracker.MarkPersistentEntity(id);
        return true;
    }

    private static void Insert(List<PersistentEntity> list, PersistentEntity entity)
    {
        var index = list.Count;
        while (index > 0 && list[index - 1].Id.Value > entity.Id.Value)
        {
            index--;
        }

        list.Insert(index, entity);
    }
}
