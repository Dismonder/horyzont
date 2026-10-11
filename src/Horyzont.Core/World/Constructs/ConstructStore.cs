using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;

namespace Horyzont.Core.World.Constructs;

/// <summary>All constructs of a world.</summary>
public sealed class ConstructStore
{
    private readonly Dictionary<EntityId, Construct> _byId = [];
    private readonly List<Construct> _sorted = [];
    private readonly ChangeTracker _tracker;

    internal ConstructStore(ChangeTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>Number of constructs.</summary>
    public int Count => _sorted.Count;

    /// <summary>
    /// Gets a counter that changes whenever a construct is added or removed (not on block or pose changes): indexes over
    /// <see cref="All"/> rebuild only when it differs. Runtime only, never saved or hashed.
    /// </summary>
    public ulong StructureVersion { get; private set; }

    /// <summary>All constructs in <see cref="EntityId"/> order.</summary>
    public IReadOnlyList<Construct> All => _sorted;

    /// <summary>Looks up a construct.</summary>
    /// <param name="id">Construct entity id.</param>
    /// <param name="construct">The construct when found.</param>
    /// <returns>True when it exists.</returns>
    public bool TryGet(EntityId id, [NotNullWhen(true)] out Construct? construct) => _byId.TryGetValue(id, out construct);

    /// <summary>Constructs whose parent is the given frame, in <see cref="EntityId"/> order.</summary>
    /// <param name="frame">Parent frame.</param>
    /// <returns>The constructs.</returns>
    public IEnumerable<Construct> InFrame(FrameRef frame)
    {
        foreach (var construct in _sorted)
        {
            if (construct.Parent == frame)
            {
                yield return construct;
            }
        }
    }

    /// <summary>Creates an empty construct and marks it changed.</summary>
    internal Construct Create(EntityId id, FrameRef parent, Vector3 origin, Quaternion rotation)
    {
        var construct = new Construct(id, parent, 0, ConstructMode.Static, origin, rotation, 0, _tracker);
        Add(construct);
        _tracker.MarkConstruct(id);
        return construct;
    }

    /// <summary>Adds a construct without marking it changed (world loader).</summary>
    internal Construct Load(EntityId id, FrameRef parent, uint parentSeq, ConstructMode mode, Vector3 origin, Quaternion rotation, uint version)
    {
        var construct = new Construct(id, parent, parentSeq, mode, origin, rotation, version, _tracker);
        Add(construct);
        return construct;
    }

    /// <summary>Removes a construct and marks it and all its chunks removed.</summary>
    internal void Remove(EntityId id)
    {
        if (!_byId.Remove(id, out var construct))
        {
            throw new InvalidOperationException($"Construct {id.Value} does not exist.");
        }

        _sorted.RemoveAt(IndexOf(id));
        StructureVersion++;
        _tracker.MarkConstruct(id);
        foreach (var chunk in construct.Chunks)
        {
            _tracker.MarkChunk(new ConstructChunkKey(id, chunk.Key));
        }
    }

    private void Add(Construct construct)
    {
        if (!_byId.TryAdd(construct.Id, construct))
        {
            throw new InvalidDataException($"Duplicate construct {construct.Id.Value}.");
        }

        var index = ~IndexOf(construct.Id);
        _sorted.Insert(index, construct);
        StructureVersion++;
    }

    private int IndexOf(EntityId id)
    {
        int low = 0, high = _sorted.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) >>> 1;
            var value = _sorted[mid].Id.Value;
            if (value == id.Value)
            {
                return mid;
            }

            if (value < id.Value)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return ~low;
    }
}
