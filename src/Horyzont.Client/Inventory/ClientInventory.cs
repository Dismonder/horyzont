using System.Diagnostics.CodeAnalysis;

using Horyzont.Client.Session;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Inventory;

/// <summary>
/// The containers of the local player as the server replicates them (K6-29): the bag, the hotbar and the equipment, plus the item type
/// registry. A delta applies only as the next version of a container; a gap asks the server for the whole state again (spec 03,
/// "Sieć"). The class changes only on server messages, never on the intents of the player (decision D15).
/// </summary>
public sealed class ClientInventory : IClientSessionListener
{
    private readonly ItemCatalog _catalog;
    private readonly Action<ContainerId> _requestState;
    private readonly ILogger<ClientInventory> _logger;
    private readonly Dictionary<ulong, ClientContainer> _containers = [];
    private readonly HashSet<ulong> _awaitingState = [];
    private readonly Dictionary<ushort, string> _keys = [];
    private readonly List<KeyValuePair<ItemTypeId, int>> _net = [];

    /// <summary>Initializes a new instance of the <see cref="ClientInventory"/> class.</summary>
    /// <param name="catalog">The item content of the game (the server registry gives only keys).</param>
    /// <param name="requestState">Asks the server for the whole state of a container (a <c>ContainerSubscribe</c>).</param>
    /// <param name="logger">Logger.</param>
    public ClientInventory(ItemCatalog catalog, Action<ContainerId> requestState, ILogger<ClientInventory> logger)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _requestState = requestState ?? throw new ArgumentNullException(nameof(requestState));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Raised after any state or delta changed a container (the UI redraws).</summary>
    public event Action? Changed;

    /// <summary>Raised once per item type for a delta that changed the number of items of the type held by the player (never for a whole state).</summary>
    public event Action<ItemTypeId, int>? OwnedCountChanged;

    /// <summary>Raised after the whole state of a container was stored (also the containers of chests the player opened, K6-30).</summary>
    public event Action<ClientContainer>? StateApplied;

    /// <summary>Raised when the server refused or ended the subscription to a container (a locked or distant chest, K6-30 D20).</summary>
    public event Action<ContainerId, RejectReason>? StateRefused;

    /// <summary>Gets the backpack, or null before its state arrived.</summary>
    public ClientContainer? Bag { get; private set; }

    /// <summary>Gets the hotbar, or null before its state arrived.</summary>
    public ClientContainer? Hotbar { get; private set; }

    /// <summary>Gets the equipment, or null before its state arrived.</summary>
    public ClientContainer? Equipment { get; private set; }

    /// <summary>Looks up a container.</summary>
    /// <param name="id">Container id.</param>
    /// <param name="container">The container when its state was received.</param>
    /// <returns>True when found.</returns>
    public bool TryGetContainer(ContainerId id, [NotNullWhen(true)] out ClientContainer? container) => _containers.TryGetValue(id.Value, out container);

    /// <summary>Forgets a container that the player no longer follows (a chest that was closed).</summary>
    /// <param name="id">The container; the containers of the player are never forgotten.</param>
    public void Forget(ContainerId id)
    {
        if (_containers.TryGetValue(id.Value, out var container) && container.Kind is not (ContainerKind.PlayerBag or ContainerKind.Hotbar or ContainerKind.Equipment))
        {
            _containers.Remove(id.Value);
            _awaitingState.Remove(id.Value);
            Changed?.Invoke();
        }
    }

    /// <summary>The content key of an item type id.</summary>
    /// <param name="type">Item type id.</param>
    /// <returns>The key, or null when the registry does not know the id.</returns>
    public string? KeyOf(ItemTypeId type) => _keys.TryGetValue(type.Value, out var key) ? key : null;

    /// <summary>Looks up the content definition of an item type.</summary>
    /// <param name="type">Item type id.</param>
    /// <param name="def">The definition.</param>
    /// <returns>True when the registry and the content know the type.</returns>
    public bool TryGetDef(ItemTypeId type, [NotNullWhen(true)] out ItemDef? def)
    {
        def = null;
        return KeyOf(type) is { } key && _catalog.TryGet(key, out def);
    }

    /// <summary>The type id of an item key in the server registry.</summary>
    /// <param name="key">Content key.</param>
    /// <param name="type">The id when the registry has the key.</param>
    /// <returns>True when found.</returns>
    public bool TryGetTypeId(string key, out ItemTypeId type)
    {
        ArgumentNullException.ThrowIfNull(key);
        foreach (var (id, itemKey) in _keys)
        {
            if (string.Equals(itemKey, key, StringComparison.Ordinal))
            {
                type = new ItemTypeId(id);
                return true;
            }
        }
        type = default;
        return false;
    }

    /// <summary>The number of items of a type in the bag and the hotbar.</summary>
    /// <param name="type">Item type.</param>
    /// <returns>The total count.</returns>
    public int CountOf(ItemTypeId type)
    {
        var total = 0;
        foreach (var container in new[] { Bag, Hotbar })
        {
            foreach (var stack in container?.Slots ?? [])
            {
                if (stack.Type == type)
                {
                    total += stack.Count;
                }
            }
        }

        return total;
    }

    /// <summary>
    /// True when more resource items can still be taken: some bag or hotbar slot is empty, or a stack of a resource is below its limit.
    /// True when the containers are not known yet.
    /// </summary>
    /// <returns>Whether a mined item would find room.</returns>
    public bool HasRoomForResources()
    {
        if (Bag is null || Hotbar is null)
        {
            return true;
        }

        return HasRoom(Bag) || HasRoom(Hotbar);
    }

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        switch (message)
        {
            case ItemRegistry registry:
                _keys.Clear();
                ushort max = 0;
                foreach (var entry in registry.Entries)
                {
                    _keys[entry.Id] = entry.Key;
                    max = Math.Max(max, entry.Id);
                }

                _logger.RegistryReceived(registry.Entries.Count, max);
                break;
            case ContainerState state:
                ApplyState(state);
                break;
            case ContainerDelta delta:
                ApplyDelta(delta);
                break;
        }
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code)
    {
        _containers.Clear();
        _awaitingState.Clear();
        _keys.Clear();
        Bag = null;
        Hotbar = null;
        Equipment = null;
        Changed?.Invoke();
    }

    private bool HasRoom(ClientContainer container)
    {
        foreach (var stack in container.Slots)
        {
            if (stack.IsEmpty)
            {
                return true;
            }

            if (TryGetDef(stack.Type, out var def) && def.Category == ItemCategory.Resource && stack.Count < def.MaxStack)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyState(ContainerState state)
    {
        _awaitingState.Remove(state.Container.Value);
        if (state.Reason != RejectReason.None)
        {
            _logger.StateRefused(state.Container.Value, state.Reason);
            Forget(state.Container);
            StateRefused?.Invoke(state.Container, state.Reason);
            return;
        }

        var container = new ClientContainer(state.Container, state.Kind, state.Access, state.Size, state.Version);
        foreach (var slot in state.Slots)
        {
            if (!container.TrySet(slot.Slot, slot.Stack))
            {
                _logger.SlotOutside(state.Container.Value, slot.Slot, state.Size);
            }
        }

        _containers[state.Container.Value] = container;
        switch (state.Kind)
        {
            case ContainerKind.PlayerBag:
                Bag = container;
                break;
            case ContainerKind.Hotbar:
                Hotbar = container;
                break;
            case ContainerKind.Equipment:
                Equipment = container;
                break;
        }

        _logger.StateApplied(state.Container.Value, state.Version, state.Size);
        Changed?.Invoke();
        StateApplied?.Invoke(container);
    }

    private void ApplyDelta(ContainerDelta delta)
    {
        if (!_containers.TryGetValue(delta.Container.Value, out var container))
        {
            _logger.DeltaUnknownContainer(delta.Container.Value);
            return;
        }

        if (delta.Version <= container.Version)
        {
            _logger.DeltaOld(delta.Container.Value, delta.Version, container.Version);
            return;
        }

        if (delta.Version != container.Version + 1)
        {
            _logger.DeltaGap(delta.Container.Value, delta.Version, container.Version);
            if (_awaitingState.Add(delta.Container.Value))
            {
                _requestState(delta.Container);
            }

            return;
        }

        _net.Clear();
        foreach (var change in delta.Changed)
        {
            if ((uint)change.Slot >= (uint)container.Size)
            {
                _logger.SlotOutside(delta.Container.Value, change.Slot, container.Size);
                continue;
            }

            var old = container.Get(change.Slot);
            container.TrySet(change.Slot, change.Stack);
            Add(old.Type, -old.Count);
            Add(change.Stack.Type, change.Stack.Count);
        }

        container.Version = delta.Version;
        Changed?.Invoke();

        // Only what the player carries counts for the pickup toasts; the items of a chest are not his.
        if (container.Kind is not (ContainerKind.PlayerBag or ContainerKind.Hotbar or ContainerKind.Equipment))
        {
            return;
        }

        foreach (var (type, net) in _net)
        {
            if (net != 0)
            {
                OwnedCountChanged?.Invoke(type, net);
            }
        }
    }

    private void Add(ItemTypeId type, int count)
    {
        if (type.IsNone || count == 0)
        {
            return;
        }

        for (var i = 0; i < _net.Count; i++)
        {
            if (_net[i].Key == type)
            {
                _net[i] = new KeyValuePair<ItemTypeId, int>(type, _net[i].Value + count);
                return;
            }
        }

        _net.Add(new KeyValuePair<ItemTypeId, int>(type, count));
    }
}
