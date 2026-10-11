using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Replication;

/// <summary>
/// The constructs a client holds, in a private store that exposes only the read-only queries of <see cref="ConstructStore"/>
/// (ray, overlap and pose queries run unchanged on a copy). It has no world, journal or id allocator; the only way to change it is
/// through its <see cref="ConstructReplica"/> entries.
/// </summary>
public sealed class ConstructReplicaSet
{
    private readonly ChangeTracker _tracker = new();
    private readonly ConstructStore _store;
    private readonly Dictionary<EntityId, ConstructReplica> _replicas = [];

    /// <summary>Initializes a new instance of the <see cref="ConstructReplicaSet"/> class.</summary>
    public ConstructReplicaSet() => _store = new ConstructStore(_tracker);

    /// <summary>Gets the read-only view for ray, overlap and pose queries (constructs in id order).</summary>
    public ConstructStore Store => _store;

    /// <summary>Gets the number of replicas.</summary>
    public int Count => _replicas.Count;

    /// <summary>Adds a complete copy, replacing a replica of the same construct.</summary>
    /// <param name="header">Construct header.</param>
    /// <param name="chunks">Unique chunks of the construct.</param>
    /// <returns>The new replica.</returns>
    /// <exception cref="InvalidDataException">The header or a chunk is malformed; the set is unchanged apart from the replaced replica being gone.</exception>
    public ConstructReplica Add(ConstructHeaderRecord header, IReadOnlyList<ConstructChunkRecord> chunks)
    {
        ArgumentNullException.ThrowIfNull(header);
        Remove(header.Id);
        var replica = ConstructReplica.LoadInto(_store, _tracker, header, chunks);
        _replicas.Add(header.Id, replica);
        return replica;
    }

    /// <summary>Looks up a replica.</summary>
    /// <param name="id">Construct entity.</param>
    /// <param name="replica">The replica when present.</param>
    /// <returns>True when present.</returns>
    public bool TryGet(EntityId id, [NotNullWhen(true)] out ConstructReplica? replica) => _replicas.TryGetValue(id, out replica);

    /// <summary>Removes a replica.</summary>
    /// <param name="id">Construct entity.</param>
    /// <returns>True when it existed.</returns>
    public bool Remove(EntityId id)
    {
        if (!_replicas.Remove(id))
        {
            return false;
        }

        _store.Remove(id);
        _tracker.Clear();
        return true;
    }

    /// <summary>Removes every replica.</summary>
    public void Clear()
    {
        foreach (var id in new List<EntityId>(_replicas.Keys))
        {
            Remove(id);
        }
    }
}
