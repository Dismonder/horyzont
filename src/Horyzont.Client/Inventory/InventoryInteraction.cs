using Horyzont.Client.Session;
using Horyzont.Client.Tools;
using Horyzont.Core.Content;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Inventory;

/// <summary>
/// The mouse logic of the inventory screen, independent of the engine (K6-29, decisions D13 and D15): taking a whole stack or half of
/// it in hand, placing it, shift-click moves and discarding. Nothing moves on the client: each placement is an
/// <see cref="ItemMoveIntent"/> that carries the type and the number of items the source slot held, and the slots involved wait
/// "in transit" until the reply of the server arrives.
/// </summary>
public sealed class InventoryInteraction : IClientSessionListener
{
    private readonly ClientInventory _inventory;
    private readonly IIntentSender _sender;
    private readonly ILogger<InventoryInteraction> _logger;
    private readonly Dictionary<uint, (SlotRef From, SlotRef? To)> _pending = [];
    private bool _awaitingDiscard;

    /// <summary>Initializes a new instance of the <see cref="InventoryInteraction"/> class.</summary>
    /// <param name="inventory">The inventory.</param>
    /// <param name="sender">Where the intents go.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="chests">The chest session whose open chest is the other side of a shift click (K6-30); null for none.</param>
    /// <param name="stations">The station session whose input is the other side of a shift click (K6-32); null for none.</param>
    public InventoryInteraction(
        ClientInventory inventory, IIntentSender sender, ILogger<InventoryInteraction> logger, ChestSession? chests = null, Crafting.StationSession? stations = null)
    {
        Chests = chests;
        Stations = stations;
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets or sets a value indicating whether the equipment can be taken from and put into (K6-34 D13, D20): true when the world has the hazard content; without it the server refuses every move
    /// that touches the equipment, so the client does not offer it.
    /// </summary>
    public bool EquipmentEnabled { get; set; }

    /// <summary>Gets the chest session, or null.</summary>
    public ChestSession? Chests { get; }

    /// <summary>Gets the station session, or null.</summary>
    public Crafting.StationSession? Stations { get; }

    /// <summary>Gets the items in hand, or null.</summary>
    public HeldStack? Held { get; private set; }

    /// <summary>Gets the reason of the latest refusal by the server, <see cref="RejectReason.None"/> after an accepted intent.</summary>
    public RejectReason LastRefusal { get; private set; }

    /// <summary>Gets a value indicating whether the player is asked to confirm discarding the items in hand.</summary>
    public bool AwaitingDiscardConfirmation => _awaitingDiscard && Held is not null;

    /// <summary>Gets a value indicating whether the items in hand may be discarded (not a tool, a type the content knows).</summary>
    public bool CanDiscard => Held is { } held && _inventory.TryGetDef(held.Type, out var def) && (def.Flags & ItemFlags.Tool) == 0;

    /// <summary>Gets a value indicating whether an intent waits for the server.</summary>
    public bool HasPending => _pending.Count > 0;

    /// <summary>True when a slot is source or target of an intent that the server has not answered.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>Whether the slot is in transit.</returns>
    public bool IsPending(SlotRef slot)
    {
        foreach (var (from, to) in _pending.Values)
        {
            if (from == slot || to == slot)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Left click: takes the whole stack in hand, or places the items in hand.</summary>
    /// <param name="slot">The slot under the cursor.</param>
    /// <returns>What happened.</returns>
    public InteractionOutcome LeftClick(SlotRef slot)
    {
        if (ValidateHeld())
        {
            return InteractionOutcome.Cancelled;
        }

        return Held is { } held ? Place(held, slot, held.Count) : Pick(slot, half: false);
    }

    /// <summary>Right click: takes half of the stack in hand (rounded up), or places one item from the hand.</summary>
    /// <param name="slot">The slot under the cursor.</param>
    /// <returns>What happened.</returns>
    public InteractionOutcome RightClick(SlotRef slot)
    {
        if (ValidateHeld())
        {
            return InteractionOutcome.Cancelled;
        }

        return Held is { } held ? Place(held, slot, 1) : Pick(slot, half: true);
    }

    /// <summary>
    /// Shift click: moves the whole stack to any free place of the other container: from the bag or the hotbar to the open chest or the
    /// input of the open station (K6-32), or to the other of the two without one; from a chest or a station to the bag.
    /// </summary>
    /// <param name="slot">The slot under the cursor.</param>
    /// <returns>What happened.</returns>
    public InteractionOutcome ShiftClick(SlotRef slot)
    {
        if (Held is not null || IsPending(slot) || !_sender.CanSend || !TryGetUsable(slot, out var container))
        {
            return InteractionOutcome.Ignored;
        }

        var stack = container.Get(slot.Slot);
        var other = container.Kind switch
        {
            ContainerKind.Chest or ContainerKind.StationInput or ContainerKind.StationOutput => _inventory.Bag,
            _ when Chests?.OpenContainer is { } chest => chest,
            _ when Stations?.Input is { } input => input,
            ContainerKind.PlayerBag => _inventory.Hotbar,
            _ => _inventory.Bag,
        };
        if (stack.IsEmpty || other is null)
        {
            return InteractionOutcome.Ignored;
        }

        Send(new ItemMoveIntent(slot, ItemMoveTarget.AnyIn(other.Id), stack.Count, stack.Type, stack.Count), slot, null);
        return InteractionOutcome.Sent;
    }

    /// <summary>Starts discarding the items in hand: the player must confirm.</summary>
    /// <returns>False when nothing is in hand or the items cannot be discarded (tools).</returns>
    public bool Discard()
    {
        if (!CanDiscard)
        {
            return false;
        }

        _awaitingDiscard = true;
        return true;
    }

    /// <summary>Confirms discarding: sends the drop intent for the items in hand.</summary>
    public void ConfirmDiscard()
    {
        if (!_awaitingDiscard || !CanDiscard || !_sender.CanSend || Held is not { } held)
        {
            return;
        }

        _awaitingDiscard = false;
        Held = null;
        Send(new ItemDropIntent(held.Source, held.Count, held.Type, held.SourceCount), held.Source, null);
    }

    /// <summary>Withdraws the question about discarding; the items stay in hand.</summary>
    public void CancelDiscard() => _awaitingDiscard = false;

    /// <summary>Puts the items in hand back (the screen closes, Escape).</summary>
    public void Cancel()
    {
        Held = null;
        _awaitingDiscard = false;
    }

    /// <summary>Drops the hand when the source slot is no longer what it was when the items were taken.</summary>
    /// <returns>True when the hand was cleared.</returns>
    public bool ValidateHeld()
    {
        if (Held is not { } held)
        {
            return false;
        }

        if (_inventory.TryGetContainer(held.Source.Container, out var container))
        {
            var now = container.Get(held.Source.Slot);
            if (now.Type == held.Type && now.Count == held.SourceCount)
            {
                return false;
            }
        }

        Cancel();
        return true;
    }

    /// <inheritdoc />
    public void OnIntentReply(IntentReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (!_pending.Remove(reply.ClientSeq))
        {
            return;
        }

        LastRefusal = reply.Reason;
        if (reply.Reason != RejectReason.None)
        {
            _logger.IntentRefused(reply.ClientSeq, reply.Reason);
        }
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code)
    {
        _pending.Clear();
        Cancel();
    }

    private InteractionOutcome Pick(SlotRef slot, bool half)
    {
        if (IsPending(slot) || !TryGetUsable(slot, out var container))
        {
            return InteractionOutcome.Ignored;
        }

        var stack = container.Get(slot.Slot);
        if (stack.IsEmpty)
        {
            return InteractionOutcome.Ignored;
        }

        var count = half ? (ushort)((stack.Count + 1) / 2) : stack.Count;
        Held = new HeldStack(slot, stack.Type, count, stack.Count);
        _awaitingDiscard = false;
        return InteractionOutcome.Picked;
    }

    private InteractionOutcome Place(HeldStack held, SlotRef target, ushort count)
    {
        if (target == held.Source)
        {
            Cancel();
            return InteractionOutcome.Cancelled;
        }

        if (IsPending(target) || !_sender.CanSend || !TryGetUsable(target, out var container) || container.Kind == ContainerKind.StationOutput)
        {
            // The output of a station is only taken from (K6-32 D17): the server would refuse a move into it.
            return InteractionOutcome.Ignored;
        }

        var there = container.Get(target.Slot);
        if (!EquipmentAllows(held, target, container, there, count))
        {
            return InteractionOutcome.Ignored;
        }

        var allowed = there.IsEmpty
            || (there.Type == held.Type && !IsFull(there))
            || (there.Type != held.Type && count == held.SourceCount); // a swap moves the whole source stack
        if (!allowed)
        {
            return InteractionOutcome.Ignored;
        }

        Cancel();
        Send(new ItemMoveIntent(held.Source, ItemMoveTarget.ToSlot(target), count, held.Type, held.SourceCount), held.Source, target);
        return InteractionOutcome.Sent;
    }

    // The moves that touch the equipment (K6-34 D13, D20): what goes into a slot must fit it, one item at a time, and in a swap the item that comes back must fit the slot it comes back to.
    private bool EquipmentAllows(HeldStack held, SlotRef target, ClientContainer targetContainer, ItemStack there, ushort count)
    {
        if (targetContainer.Kind == ContainerKind.Equipment
            && !(_inventory.TryGetDef(held.Type, out var heldDef) && EquipmentDropRules.CanPlace(target.Slot, heldDef, count)))
        {
            return false;
        }

        var swaps = !there.IsEmpty && there.Type != held.Type;
        if (swaps && _inventory.TryGetContainer(held.Source.Container, out var source) && source.Kind == ContainerKind.Equipment)
        {
            return _inventory.TryGetDef(there.Type, out var thereDef) && EquipmentDropRules.CanPlace(held.Source.Slot, thereDef, there.Count);
        }

        return true;
    }

    private bool IsFull(ItemStack stack) => _inventory.TryGetDef(stack.Type, out var def) && stack.Count >= def.MaxStack;

    private bool TryGetUsable(SlotRef slot, out ClientContainer container)
    {
        if (_inventory.TryGetContainer(slot.Container, out var found)
            && (found.Kind is ContainerKind.PlayerBag or ContainerKind.Hotbar or ContainerKind.Chest or ContainerKind.StationInput or ContainerKind.StationOutput
                || (EquipmentEnabled && found.Kind == ContainerKind.Equipment))
            && slot.Slot < found.Size)
        {
            container = found;
            return true;
        }

        container = null!;
        return false;
    }

    private void Send(Intent intent, SlotRef from, SlotRef? to)
    {
        var seq = _sender.Submit(intent);
        _pending[seq] = (from, to);
        _logger.IntentSent(seq, intent);
    }
}
