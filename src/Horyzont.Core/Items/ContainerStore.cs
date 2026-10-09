using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;

namespace Horyzont.Core.Items;

/// <summary>Containers of a world, sorted by id, with an index player × kind for <see cref="ContainerHostKind.Player"/> hosts.</summary>
public sealed class ContainerStore
{
    private readonly ChangeTracker _tracker;
    private readonly Dictionary<ContainerId, Container> _byId = [];
    private readonly Dictionary<(PlayerId Player, ContainerKind Kind), Container> _byPlayer = [];
    private readonly List<Container> _sorted = [];

    internal ContainerStore(ChangeTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>Number of containers.</summary>
    public int Count => _sorted.Count;

    /// <summary>All containers sorted by id.</summary>
    public IReadOnlyList<Container> All => _sorted;

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

    /// <summary>Adds a container (operation or load); the caller checked that the id and the player × kind pair are free.</summary>
    internal Container Add(ContainerId id, ContainerKind kind, ContainerHost host, PlayerId owner, ContainerAccess access, ushort size, uint version)
    {
        var container = new Container(id, kind, host, owner, access, size, version);
        _byId.Add(id, container);
        if (host.Kind == ContainerHostKind.Player)
        {
            _byPlayer.Add((host.PlayerId, kind), container);
        }

        var index = _sorted.Count;
        while (index > 0 && _sorted[index - 1].Id.Value > id.Value)
        {
            index--;
        }

        _sorted.Insert(index, container);
        return container;
    }

    /// <summary>Removes a container (undo of a refused compound operation).</summary>
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

        _sorted.Remove(container);
    }

    /// <summary>Marks a container changed for the next checkpoint.</summary>
    internal void MarkChanged(ContainerId id) => _tracker.MarkContainer(id);
}
