using Horyzont.Client.Session;
using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Inventory;

/// <summary>
/// The chest that the local player has open (K6-30, D9 and D20): subscribes to its container, forgets it when the player closes the
/// panel or the server ends the subscription (the player walked away, the chest became private or was removed), and sends the
/// intents of the panel (take all, put matching, access). Remembers the chests the server refused as locked. Like the rest of the
/// inventory it changes only on server messages and on the explicit calls of the player.
/// </summary>
public sealed class ChestSession : IClientSessionListener
{
    private readonly ClientInventory _inventory;
    private readonly IIntentSender _sender;
    private readonly Action<Message> _send;
    private readonly ILogger<ChestSession> _logger;
    private readonly HashSet<ulong> _locked = [];
    private readonly HashSet<ulong> _notOwner = [];
    private readonly Dictionary<uint, PanelIntent> _pending = [];
    private Phase _phase;
    private ContainerId _container;
    private EntityId _construct;
    private CellPos _cell;

    /// <summary>Initializes a new instance of the <see cref="ChestSession"/> class.</summary>
    /// <param name="inventory">The inventory that stores the container.</param>
    /// <param name="sender">Where intents go.</param>
    /// <param name="send">Sends a client message (subscribe, unsubscribe).</param>
    /// <param name="logger">Logger.</param>
    public ChestSession(ClientInventory inventory, IIntentSender sender, Action<Message> send, ILogger<ChestSession> logger)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inventory.StateApplied += OnState;
        _inventory.StateRefused += OnRefused;
    }

    private enum Phase : byte
    {
        Closed = 0,
        Opening = 1,
        Open = 2,
    }

    private enum PanelIntent : byte
    {
        QuickStack = 1,
        TakeAll = 2,
        Access = 3,
    }

    /// <summary>Raised when the state of the requested chest arrived and the panel may open.</summary>
    public event Action? Opened;

    /// <summary>Raised when the open chest was closed by the player or the server (or the session ended).</summary>
    public event Action? Closed;

    /// <summary>Raised whenever something the panel shows changed (the phase, a refusal).</summary>
    public event Action? Changed;

    /// <summary>Gets a value indicating whether the state of a chest arrived and the panel is open.</summary>
    public bool IsOpen => _phase == Phase.Open;

    /// <summary>Gets a value indicating whether a chest was requested and its state has not arrived.</summary>
    public bool IsOpening => _phase == Phase.Opening;

    /// <summary>Gets the container of the requested or open chest.</summary>
    public ContainerId Container => _container;

    /// <summary>Gets the construct of the chest block.</summary>
    public EntityId Construct => _construct;

    /// <summary>Gets the cell of the chest block.</summary>
    public CellPos Cell => _cell;

    /// <summary>Gets the replicated container of the open chest, or null.</summary>
    public ClientContainer? OpenContainer => IsOpen && _inventory.TryGetContainer(_container, out var container) ? container : null;

    /// <summary>Gets the reason of the latest refusal of the server for the chest or its panel, <see cref="RejectReason.None"/> after a success.</summary>
    public RejectReason LastRefusal { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the access switch is on offer. The state of a container does not name its owner, so the switch
    /// is offered for every open chest until the server answers that the chest is not the player's.
    /// </summary>
    public bool CanConfigureAccess => IsOpen && !_notOwner.Contains(_container.Value);

    /// <summary>True when the server refused the subscription to a container as locked.</summary>
    /// <param name="container">The container.</param>
    /// <returns>Whether the chest is known to be locked for the player.</returns>
    public bool IsLocked(ContainerId container) => _locked.Contains(container.Value);

    /// <summary>Asks for a chest; closes the chest that is open.</summary>
    /// <param name="container">Container of the chest.</param>
    /// <param name="construct">Construct of the chest block.</param>
    /// <param name="cell">Cell of the chest block.</param>
    public void Open(ContainerId container, EntityId construct, CellPos cell)
    {
        if (container.IsNone)
        {
            throw new ArgumentException("A chest has a container.", nameof(container));
        }

        if (_phase == Phase.Open && container == _container)
        {
            return;
        }

        if (_phase != Phase.Closed && container != _container)
        {
            Close();
        }

        _container = container;
        _construct = construct;
        _cell = cell;
        _phase = Phase.Opening;
        LastRefusal = RejectReason.None;
        _send(new ContainerSubscribe(container));
        _logger.ChestRequested(container.Value);
        Changed?.Invoke();
    }

    /// <summary>Closes the chest and tells the server.</summary>
    public void Close()
    {
        if (_phase == Phase.Closed)
        {
            return;
        }

        var wasOpen = _phase == Phase.Open;
        var container = _container;
        _phase = Phase.Closed;
        LastRefusal = RejectReason.None;
        _pending.Clear();
        _send(new ContainerUnsubscribe(container));
        _inventory.Forget(container);
        _logger.ChestClosed(container.Value);
        if (wasOpen)
        {
            Closed?.Invoke();
        }

        Changed?.Invoke();
    }

    /// <summary>Sends the quick stack intent (put matching items into the chests nearby).</summary>
    /// <returns>True when an intent was sent.</returns>
    public bool QuickStack() => Submit(new QuickStackIntent(), PanelIntent.QuickStack);

    /// <summary>Sends the take all intent for the open chest.</summary>
    /// <returns>True when an intent was sent.</returns>
    public bool TakeAll() => IsOpen && Submit(new TakeAllIntent(_container), PanelIntent.TakeAll);

    /// <summary>Switches the chest between shared and private.</summary>
    /// <returns>True when an intent was sent.</returns>
    public bool ToggleAccess()
    {
        if (!CanConfigureAccess || OpenContainer is not { } chest)
        {
            return false;
        }

        var access = chest.Access == ContainerAccess.Private ? ContainerAccess.Shared : ContainerAccess.Private;
        return Submit(new BlockConfigIntent(_construct, _cell, new ContainerAccessConfig(access)), PanelIntent.Access);
    }

    /// <inheritdoc />
    public void OnIntentReply(IntentReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (!_pending.Remove(reply.ClientSeq, out var intent))
        {
            return;
        }

        LastRefusal = reply.Reason;
        if (intent == PanelIntent.Access && reply.Reason == RejectReason.NotOwner)
        {
            _notOwner.Add(_container.Value);
        }

        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code)
    {
        var wasOpen = _phase == Phase.Open;
        _phase = Phase.Closed;
        _pending.Clear();
        _locked.Clear();
        _notOwner.Clear();
        LastRefusal = RejectReason.None;
        if (wasOpen)
        {
            Closed?.Invoke();
        }

        Changed?.Invoke();
    }

    private bool Submit(Intent intent, PanelIntent kind)
    {
        if (!_sender.CanSend)
        {
            return false;
        }

        _pending[_sender.Submit(intent)] = kind;
        LastRefusal = RejectReason.None;
        Changed?.Invoke();
        return true;
    }

    private void OnState(ClientContainer container)
    {
        if (container.Kind != ContainerKind.Chest)
        {
            return;
        }

        if (_phase == Phase.Closed || container.Id != _container)
        {
            // A state that arrives after the panel was closed (or for another chest): not followed any more.
            _inventory.Forget(container.Id);
            return;
        }

        var opened = _phase == Phase.Opening;
        _phase = Phase.Open;
        _locked.Remove(container.Id.Value);
        LastRefusal = RejectReason.None;
        if (opened)
        {
            _logger.ChestOpened(container.Id.Value);
            Opened?.Invoke();
        }

        Changed?.Invoke();
    }

    private void OnRefused(ContainerId container, RejectReason reason)
    {
        if (reason == RejectReason.ContainerLocked)
        {
            // Also a chest that was removed or made private while it was open: the key asks again when pressed.
            _locked.Add(container.Value);
        }

        if (_phase == Phase.Closed || container != _container)
        {
            return;
        }

        var wasOpen = _phase == Phase.Open;
        _phase = Phase.Closed;
        _pending.Clear();
        LastRefusal = reason;
        _logger.ChestRefused(container.Value, reason);
        if (wasOpen)
        {
            Closed?.Invoke();
        }

        Changed?.Invoke();
    }
}
