using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Items;

/// <summary>Containers of a world, sorted by id, with an index player × kind for <see cref="ContainerHostKind.Player"/> hosts.</summary>
public sealed class ContainerStore
{
    private readonly ChangeTracker _tracker;
    private readonly Dictionary<ContainerId, Container> _byId = [];
    private readonly Dictionary<(PlayerId Player, ContainerKind Kind), Container> _byPlayer = [];
    private readonly Dictionary<(EntityId Construct, CellPos Cell, ContainerKind Kind), Container> _byBlock = [];
    private readonly Dictionary<BlockRef, int> _blockHostCount = [];
    private readonly List<Container> _sorted = [];
    private readonly Dictionary<ContainerKind, List<Container>> _byKind = [];
    private readonly List<Container> _noContainers = [];
    private readonly Dictionary<EntityId, Dictionary<ChunkPos, List<Container>>> _chestsByConstruct = [];

    internal ContainerStore(ChangeTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>Number of containers.</summary>
    public int Count => _sorted.Count;

    /// <summary>All containers sorted by id.</summary>
    public IReadOnlyList<Container> All => _sorted;

    /// <summary>Containers of one kind sorted by id: the index that lets a resolver visit the chests without scanning every container of the world.</summary>
    /// <param name="kind">Container kind.</param>
    /// <returns>The containers of that kind; empty when there are none.</returns>
    public IReadOnlyList<Container> OfKind(ContainerKind kind) => _byKind.TryGetValue(kind, out var list) ? list : _noContainers;

    /// <summary>Whether the construct hosts any chests, without visiting its containers.</summary>
    internal bool HasChests(EntityId construct) => _chestsByConstruct.ContainsKey(construct);

    /// <summary>Chests in one local chunk; movement of the construct does not invalidate this index.</summary>
    internal IReadOnlyList<Container> ChestsInChunk(EntityId construct, ChunkPos chunk) =>
        _chestsByConstruct.TryGetValue(construct, out var chunks) && chunks.TryGetValue(chunk, out var chests) ? chests : _noContainers;

    /// <summary>Looks up a container.</summary>
    /// <param name="id">Container id.</param>
    /// <param name="container">The container when found.</param>
    /// <returns>True when it exists.</returns>
    public bool TryGet(ContainerId id, [NotNullWhen(true)] out Container? container) => _byId.TryGetValue(id, out container);

    /// <summary>Looks up the container of a kind hosted by a player.</summary>
    /// <param name="player">The player.</param>
    /// <param name="kind">Container kind.</param>
    /// <param name="container">The container when found.</param>
    /// <returns>True when the player has one.</returns>
    public bool TryGetOfPlayer(PlayerId player, ContainerKind kind, [NotNullWhen(true)] out Container? container) =>
        _byPlayer.TryGetValue((player, kind), out container);

    /// <summary>Looks up the container of a kind hosted by a construct block (the index host × kind of K6-30).</summary>
    /// <param name="construct">The construct.</param>
    /// <param name="cell">Cell of the block.</param>
    /// <param name="kind">Container kind.</param>
    /// <param name="container">The container when found.</param>
    /// <returns>True when the block hosts one of that kind.</returns>
    public bool TryGetOfBlock(EntityId construct, CellPos cell, ContainerKind kind, [NotNullWhen(true)] out Container? container) =>
        _byBlock.TryGetValue((construct, cell, kind), out container);

    /// <summary>True when the block hosts a container of any kind.</summary>
    /// <param name="construct">The construct.</param>
    /// <param name="cell">Cell of the block.</param>
    /// <returns>Whether a container is hosted.</returns>
    public bool IsBlockHost(EntityId construct, CellPos cell) => _blockHostCount.ContainsKey(new BlockRef(construct, cell));

    /// <summary>Adds a container (operation or load); the caller checked that the id and the player × kind pair are free.</summary>
    internal Container Add(ContainerId id, ContainerKind kind, ContainerHost host, PlayerId owner, ContainerAccess access, ushort size, uint version)
    {
        var container = new Container(id, kind, host, owner, access, size, version);
        Insert(container);
        return container;
    }

    /// <summary>Puts a removed container back (undo of a refused compound operation); the same instance, so earlier slot undo records stay valid.</summary>
    internal void Restore(Container container) => Insert(container);

    private void Insert(Container container)
    {
        var id = container.Id;
        var host = container.Host;
        var kind = container.Kind;
        _byId.Add(id, container);
        if (host.Kind == ContainerHostKind.Player)
        {
            _byPlayer.Add((host.PlayerId, kind), container);
        }
        else if (host.Kind == ContainerHostKind.ConstructBlock)
        {
            var construct = new EntityId(host.Id);
            _byBlock.Add((construct, host.Cell, kind), container);
            if (kind == ContainerKind.Chest)
            {
                if (!_chestsByConstruct.TryGetValue(construct, out var chunks))
                {
                    chunks = [];
                    _chestsByConstruct.Add(construct, chunks);
                }

                if (!chunks.TryGetValue(host.Cell.Chunk, out var chests))
                {
                    chests = [];
                    chunks.Add(host.Cell.Chunk, chests);
                }

                InsertSorted(chests, container);
            }

            _blockHostCount[new BlockRef(construct, host.Cell)] = _blockHostCount.GetValueOrDefault(new BlockRef(construct, host.Cell)) + 1;
        }

        InsertSorted(_sorted, container);
        if (!_byKind.TryGetValue(kind, out var ofKind))
        {
            ofKind = [];
            _byKind.Add(kind, ofKind);
        }

        InsertSorted(ofKind, container);
    }

    private static void InsertSorted(List<Container> list, Container container)
    {
        var index = list.Count;
        while (index > 0 && list[index - 1].Id.Value > container.Id.Value)
        {
            index--;
        }

        list.Insert(index, container);
    }

    /// <summary>Removes a container (operation 36 or undo of a refused compound operation) and frees its host index.</summary>
    internal void Remove(ContainerId id)
    {
        if (!_byId.Remove(id, out var container))
        {
            return;
        }

        if (container.Host.Kind == ContainerHostKind.Player)
        {
            _byPlayer.Remove((container.Host.PlayerId, container.Kind));
        }
        else if (container.Host.Kind == ContainerHostKind.ConstructBlock)
        {
            var construct = new EntityId(container.Host.Id);
            _byBlock.Remove((construct, container.Host.Cell, container.Kind));
            if (container.Kind == ContainerKind.Chest)
            {
                var chunks = _chestsByConstruct[construct];
                var chests = chunks[container.Host.Cell.Chunk];
                chests.Remove(container);
                if (chests.Count == 0)
                {
                    chunks.Remove(container.Host.Cell.Chunk);
                    if (chunks.Count == 0)
                    {
                        _chestsByConstruct.Remove(construct);
                    }
                }
            }

            var left = _blockHostCount[new BlockRef(construct, container.Host.Cell)] - 1;
            if (left == 0)
            {
                _blockHostCount.Remove(new BlockRef(construct, container.Host.Cell));
            }
            else
            {
                _blockHostCount[new BlockRef(construct, container.Host.Cell)] = left;
            }
        }

        _sorted.Remove(container);
        _byKind[container.Kind].Remove(container);
    }

    /// <summary>Marks a container changed for the next checkpoint.</summary>
    internal void MarkChanged(ContainerId id) => _tracker.MarkContainer(id);
}
