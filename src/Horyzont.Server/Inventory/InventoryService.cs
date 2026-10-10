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
/// to that player only (<see cref="ContainerDelta"/> from <see cref="ServerSimulation.Applied"/>). Main thread only; changes the world
/// only through server operations of the simulation.
/// </summary>
public sealed class InventoryService : IServerPlayerObserver, IServerClientMessageHandler
{
    private static readonly ContainerKind[] _playerKinds = [ContainerKind.PlayerBag, ContainerKind.Hotbar, ContainerKind.Equipment];

    private readonly Func<ServerSimulation> _simulation;
    private readonly Action<PlayerId, Message> _send;
    private readonly Func<bool> _isReadOnly;
    private readonly ILogger<InventoryService> _logger;
    private readonly HashSet<PlayerId> _subscribed = [];
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
        return service;
    }

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player)
    {
        var sim = _simulation();
        Attach(sim);
        var state = sim.State;
        if (!HasAnyContainer(state, player))
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
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason) => _subscribed.Remove(player);

    /// <inheritdoc />
    public bool Handle(PlayerId player, Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message is not ContainerSubscribe subscribe)
        {
            // ContainerUnsubscribe: the player containers are always subscribed (D20), so there is nothing to undo.
            return message is ContainerUnsubscribe;
        }

        var state = _simulation().State;
        if (state.Containers.TryGet(subscribe.Container, out var container) && IsOwnedBy(container, player))
        {
            _send(player, StateOf(container));
            _logger.SubscribeHandled(player.Value, subscribe.Container.Value, "state");
            return true;
        }

        _send(player, ContainerState.Rejected(subscribe.Container, RejectReason.ContainerLocked));
        _logger.SubscribeHandled(player.Value, subscribe.Container.Value, "locked");
        return true;
    }

    private static bool IsOwnedBy(Container container, PlayerId player) =>
        container.Owner == player && container.Host is { Kind: ContainerHostKind.Player } host && host.PlayerId == player;

    private static bool HasAnyContainer(WorldState state, PlayerId player)
    {
        foreach (var kind in _playerKinds)
        {
            if (state.Containers.TryGetOfPlayer(player, kind, out _))
            {
                return true;
            }
        }

        return false;
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
        var first = state.ContainerIds.PeekNext().Value;
        var bag = new ContainerId(first);
        var hotbar = new ContainerId(first + 1);
        var equipment = new ContainerId(first + 2);
        var host = ContainerHost.Player(player);
        var components = new List<Operation>(3 + kit.Entries.Count)
        {
            new ContainerCreateOp(bag, ContainerKind.PlayerBag, host, player, ContainerAccess.Private, kit.BagSize),
            new ContainerCreateOp(hotbar, ContainerKind.Hotbar, host, player, ContainerAccess.Private, kit.HotbarSize),
            new ContainerCreateOp(equipment, ContainerKind.Equipment, host, player, ContainerAccess.Private, kit.EquipmentSize),
        };
        var items = 0;
        foreach (var entry in kit.Entries)
        {
            if (!content.Items.TryGet(entry.ItemKey, out var def) || !state.ItemTypes.TryGetId(entry.ItemKey, out var type))
            {
                _logger.StartKitItemUnknown(player.Value, entry.ItemKey);
                continue;
            }

            var target = entry.Container switch
            {
                ContainerKind.PlayerBag => bag,
                ContainerKind.Hotbar => hotbar,
                _ => equipment,
            };
            components.Add(new ItemCreateOp(new SlotRef(target, entry.Slot), type, entry.Count, def.MaxStack, ItemSource.Starter));
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
        if (containers.Count == 0 || _subscribed.Count == 0)
        {
            return;
        }

        var sim = _attached!;
        foreach (var change in containers)
        {
            // Version 1 is the operation that created the container: the owner learns of it through a state, not a delta.
            if (change.Version <= 1 || !sim.State.Containers.TryGet(change.Container, out var container)
                || !_subscribed.Contains(container.Owner))
            {
                continue;
            }

            var changed = new List<SlotContent>(change.Slots.Count);
            foreach (var slot in change.Slots)
            {
                changed.Add(new SlotContent(slot, container.GetSlot(slot)));
            }

            _send(container.Owner, new ContainerDelta(container.Id, change.Version, changed));
            _logger.DeltaSent(container.Owner.Value, container.Id.Value, change.Version, applied.Id.Value);
        }
    }
}
