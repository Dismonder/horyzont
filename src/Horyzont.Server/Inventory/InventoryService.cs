using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Inventory;

/// <summary>
/// The player inventory on the server (K6-29W stage C, D6, D20): creates the bag, hotbar and equipment with the start kit when a player
/// without containers is ready, sends the item registry and the container states, and replicates every change of a player's containers
/// to that player only (<see cref="ContainerDelta"/> from <see cref="ServerSimulation.Applied"/>). K6-30 D20: a player may also subscribe to
/// the container of a chest block within 6 m, keeps the subscription up to 8 m and gets every change of it; the service ends a
/// subscription (always with a <see cref="ContainerState"/> that carries the refusal) when the player walks away, loses access or the
/// container is removed. Main thread only; changes the world only through server operations of the simulation.
/// </summary>
public sealed class InventoryService : IServerPlayerObserver, IServerClientMessageHandler, IServerTickParticipant
{
    /// <summary>Ticks between two checks of the chest subscriptions.</summary>
    public const int SubscriptionCheckEveryTicks = 10;

    private static readonly ContainerKind[] _playerKinds = [ContainerKind.PlayerBag, ContainerKind.Hotbar, ContainerKind.Equipment];

    private readonly Func<ServerSimulation> _simulation;
    private readonly Action<PlayerId, Message> _send;
    private readonly Func<bool> _isReadOnly;
    private readonly ILogger<InventoryService> _logger;
    private readonly HashSet<PlayerId> _subscribed = [];
    private readonly Dictionary<ContainerId, List<PlayerId>> _chestSubscribers = [];
    private readonly List<(ContainerId Container, PlayerId Player)> _scratch = [];
    private ServerSimulation? _attached;

    /// <summary>Initializes a new instance of the <see cref="InventoryService"/> class.</summary>
    /// <param name="simulation">The simulation of the running world; asked when the first player is ready.</param>
    /// <param name="send">Sends a game message to a player of the session.</param>
    /// <param name="isReadOnly">True when the server must not change the world (safe mode, shutdown): nothing is created then.</param>
    /// <param name="logger">Logger.</param>
    public InventoryService(
        Func<ServerSimulation> simulation, Action<PlayerId, Message> send, Func<bool> isReadOnly, ILogger<InventoryService> logger)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(isReadOnly);
        ArgumentNullException.ThrowIfNull(logger);
        _simulation = simulation;
        _send = send;
        _isReadOnly = isReadOnly;
        _logger = logger;
    }

    /// <summary>Creates the service for a server and registers it as player observer and message handler; call before the world starts.</summary>
    /// <param name="runtime">The server.</param>
    /// <param name="loggers">Loggers.</param>
    /// <returns>The service.</returns>
    public static InventoryService Register(ServerRuntime runtime, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(loggers);
        var service = new InventoryService(
            () => runtime.Simulation,
            (player, message) => runtime.Session.Send(player, message),
            () => runtime.RuntimeState != ServerRuntimeState.Running,
            loggers.CreateLogger<InventoryService>());
        runtime.AddPlayerObserver(service);
        runtime.AddClientMessageHandler(service);
        runtime.AddTickParticipant(service);
        return service;
    }

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player)
    {
        var sim = _simulation();
        Attach(sim);
        var state = sim.State;
        if (!HasAllContainers(state, player))
        {
            if (_isReadOnly())
            {
                _logger.ReadOnlyNoContainers(player.Value);
            }
            else if (sim.Survival is { } content && state.Players.TryGet(player, out var joined) && joined.IsOnline)
            {
                CreateContainers(sim, content, player);
            }
        }

        _send(player, new ItemRegistry(state.ItemTypes.Entries));
        var found = 0;
        foreach (var kind in _playerKinds)
        {
            if (state.Containers.TryGetOfPlayer(player, kind, out var container))
            {
                _send(player, StateOf(container));
                found++;
            }
        }

        if (found is > 0 and < 3)
        {
            _logger.ContainersIncomplete(player.Value);
        }

        _subscribed.Add(player);
    }

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason)
    {
        _subscribed.Remove(player);
        foreach (var subscribers in _chestSubscribers.Values)
        {
            subscribers.Remove(player);
        }
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (_attached is { } sim && _chestSubscribers.Count > 0 && sim.State.CurrentTick.Value % SubscriptionCheckEveryTicks == 0)
        {
            CheckSubscriptions();
        }
    }

    /// <inheritdoc />
    public bool Handle(PlayerId player, Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message is ContainerUnsubscribe unsubscribe)
        {
            // The player containers are always subscribed (D20 of K6-29W), so only a chest subscription ends here.
            RemoveSubscriber(unsubscribe.Container, player);
            return true;
        }

        if (message is not ContainerSubscribe subscribe)
        {
            return false;
        }

        var sim = _simulation();
        var state = sim.State;
        if (!state.Containers.TryGet(subscribe.Container, out var container))
        {
            Reject(player, subscribe.Container, RejectReason.ContainerLocked, "locked");
            return true;
        }

        if (IsOwnedBy(container, player))
        {
            _send(player, StateOf(container));
            _logger.SubscribeHandled(player.Value, subscribe.Container.Value, "state");
            return true;
        }

        if (container.Kind != ContainerKind.Chest || container.Host.Kind != ContainerHostKind.ConstructBlock || !HasAccess(container, player))
        {
            Reject(player, subscribe.Container, RejectReason.ContainerLocked, "locked");
            return true;
        }

        if (!ContainerReach.TryDistanceFromEye(state, sim.Rules, player, new EntityId(container.Host.Id), container.Host.Cell, out var meters)
            || meters > sim.Rules.ContainerOpenRangeMeters)
        {
            Reject(player, subscribe.Container, RejectReason.OutOfReach, "out of reach");
            return true;
        }

        if (!_chestSubscribers.TryGetValue(container.Id, out var subscribers))
        {
            subscribers = [];
            _chestSubscribers.Add(container.Id, subscribers);
        }

        if (!subscribers.Contains(player))
        {
            subscribers.Add(player);
        }

        _send(player, StateOf(container));
        _logger.SubscribeHandled(player.Value, subscribe.Container.Value, "chest state");
        return true;
    }

    /// <summary>
    /// Checks the chest subscriptions (K6-30 D20): ends those of a player who walked further than 8 m, lost access or whose container is
    /// gone, each with a <see cref="ContainerState"/> that carries the refusal. Called every 10th tick.
    /// </summary>
    public void CheckSubscriptions()
    {
        if (_attached is not { } sim || _chestSubscribers.Count == 0)
        {
            return;
        }

        var state = sim.State;
        _scratch.Clear();
        foreach (var pair in _chestSubscribers)
        {
            foreach (var player in pair.Value)
            {
                _scratch.Add((pair.Key, player));
            }
        }

        // The subscriptions are visited in container and subscription order of the dictionary; the messages are independent of each other.
        foreach (var (containerId, player) in _scratch)
        {
            var reason = RejectReason.None;
            if (!state.Containers.TryGet(containerId, out var container))
            {
                reason = RejectReason.ContainerLocked;
            }
            else if (!HasAccess(container, player))
            {
                reason = RejectReason.ContainerLocked;
            }
            else if (container.Host.Kind != ContainerHostKind.ConstructBlock
                || !ContainerReach.TryDistanceFromEye(state, sim.Rules, player, new EntityId(container.Host.Id), container.Host.Cell, out var meters)
                || meters > sim.Rules.ContainerKeepRangeMeters)
            {
                reason = RejectReason.OutOfReach;
            }

            if (reason != RejectReason.None)
            {
                RemoveSubscriber(containerId, player);
                _send(player, ContainerState.Rejected(containerId, reason));
                _logger.SubscriptionEnded(player.Value, containerId.Value, reason);
            }
        }

        _scratch.Clear();
    }

    private static bool HasAccess(Container container, PlayerId player) =>
        container.Access == ContainerAccess.Shared || container.Owner == player;

    private void Reject(PlayerId player, ContainerId container, RejectReason reason, string result)
    {
        _send(player, ContainerState.Rejected(container, reason));
        _logger.SubscribeHandled(player.Value, container.Value, result);
    }

    private void RemoveSubscriber(ContainerId container, PlayerId player)
    {
        if (_chestSubscribers.TryGetValue(container, out var subscribers) && subscribers.Remove(player) && subscribers.Count == 0)
        {
            _chestSubscribers.Remove(container);
        }
    }

    private static bool IsOwnedBy(Container container, PlayerId player) =>
        container.Owner == player && container.Host is { Kind: ContainerHostKind.Player } host && host.PlayerId == player;

    private static bool HasAllContainers(WorldState state, PlayerId player)
    {
        foreach (var kind in _playerKinds)
        {
            if (!state.Containers.TryGetOfPlayer(player, kind, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static ContainerState StateOf(Container container)
    {
        var slots = new List<SlotContent>();
        for (var slot = 0; slot < container.Size; slot++)
        {
            var stack = container.GetSlot(slot);
            if (!stack.IsEmpty)
            {
                slots.Add(new SlotContent((ushort)slot, stack));
            }
        }

        return new ContainerState(container.Id, RejectReason.None, container.Kind, container.Access, (ushort)container.Size, container.Version, slots);
    }

    private void Attach(ServerSimulation sim)
    {
        if (ReferenceEquals(_attached, sim))
        {
            return;
        }

        _attached = sim;
        sim.Applied += OnApplied;
    }

    private void CreateContainers(ServerSimulation sim, SurvivalContent content, PlayerId player)
    {
        var state = sim.State;
        var kit = content.StartKit;
        var next = state.ContainerIds.PeekNext().Value;
        var host = ContainerHost.Player(player);
        var components = new List<Operation>(3 + kit.Entries.Count);
        var created = new ContainerKind[3];
        var targets = new ContainerId[3];
        var createdCount = 0;

        // Only the missing containers are created (a partial set must not stay broken); items go into the new ones only.
        foreach (var kind in _playerKinds)
        {
            if (state.Containers.TryGetOfPlayer(player, kind, out _))
            {
                continue;
            }

            var id = new ContainerId(next++);
            var size = kind switch
            {
                ContainerKind.PlayerBag => kit.BagSize,
                ContainerKind.Hotbar => kit.HotbarSize,
                _ => kit.EquipmentSize,
            };
            components.Add(new ContainerCreateOp(id, kind, host, player, ContainerAccess.Private, size));
            created[createdCount] = kind;
            targets[createdCount++] = id;
        }

        var items = 0;
        foreach (var entry in kit.Entries)
        {
            var targetIndex = Array.IndexOf(created, entry.Container, 0, createdCount);
            if (targetIndex < 0)
            {
                continue;
            }

            if (!content.Items.TryGet(entry.ItemKey, out var def) || !state.ItemTypes.TryGetId(entry.ItemKey, out var type))
            {
                _logger.StartKitItemUnknown(player.Value, entry.ItemKey);
                continue;
            }

            components.Add(new ItemCreateOp(new SlotRef(targets[targetIndex], entry.Slot), type, entry.Count, def.MaxStack, ItemSource.Starter));
            items++;
        }

        try
        {
            var applied = sim.ApplyServerOperation(new CompoundOp(components));
            _logger.ContainersCreated(player.Value, applied.Id.Value, items);
        }
#pragma warning disable CA1031 // A broken precondition (nothing changed) or a faulted simulation must not stop the session of the others; it is logged and the tick faults the server if the simulation is faulted.
        catch (InvalidOperationException ex)
#pragma warning restore CA1031
        {
            _logger.ContainerCreationFailed(player.Value, ex);
        }
    }

    private void OnApplied(AppliedOperation applied)
    {
        var containers = applied.ContainerChanges;
        if (containers.Count == 0 || (_subscribed.Count == 0 && _chestSubscribers.Count == 0))
        {
            return;
        }

        var sim = _attached!;
        foreach (var change in containers)
        {
            if (change.Removed)
            {
                EndChestSubscriptions(change.Container, RejectReason.ContainerLocked);
                continue;
            }

            if (!sim.State.Containers.TryGet(change.Container, out var container))
            {
                continue;
            }

            if (container.Host.Kind == ContainerHostKind.ConstructBlock)
            {
                SendChestChange(container, change, applied.Id.Value);
                continue;
            }

            // Version 1 is the operation that created the container: the owner learns of it through a state, not a delta.
            if (change.Version <= 1 || !_subscribed.Contains(container.Owner))
            {
                continue;
            }

            SendDelta(container.Owner, container, change, applied.Id.Value);
        }
    }

    private void SendDelta(PlayerId player, Container container, ContainerChange change, ulong opId)
    {
        var changed = new List<SlotContent>(change.Slots.Count);
        foreach (var slot in change.Slots)
        {
            changed.Add(new SlotContent(slot, container.GetSlot(slot)));
        }

        _send(player, new ContainerDelta(container.Id, change.Version, changed));
        _logger.DeltaSent(player.Value, container.Id.Value, change.Version, opId);
    }

    /// <summary>Sends a change of a chest container to its subscribers: slots as a delta, a change of size or access (operation 37, also together with slots) as the whole state.</summary>
    private void SendChestChange(Container container, ContainerChange change, ulong opId)
    {
        if (change.Version <= 1 || !_chestSubscribers.TryGetValue(container.Id, out var subscribers))
        {
            return;
        }

        // Copied: ending a subscription changes the list.
        var targets = subscribers.ToArray();
        var configured = change.Configured;
        foreach (var player in targets)
        {
            if (!HasAccess(container, player))
            {
                RemoveSubscriber(container.Id, player);
                _send(player, ContainerState.Rejected(container.Id, RejectReason.ContainerLocked));
                _logger.SubscriptionEnded(player.Value, container.Id.Value, RejectReason.ContainerLocked);
            }
            else if (configured)
            {
                _send(player, StateOf(container));
            }
            else
            {
                SendDelta(player, container, change, opId);
            }
        }
    }

    private void EndChestSubscriptions(ContainerId container, RejectReason reason)
    {
        if (!_chestSubscribers.Remove(container, out var subscribers))
        {
            return;
        }

        foreach (var player in subscribers)
        {
            _send(player, ContainerState.Rejected(container, reason));
            _logger.SubscriptionEnded(player.Value, container.Value, reason);
        }
    }
}
