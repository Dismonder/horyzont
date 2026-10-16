using Horyzont.Client.Session;
using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Inventory;

/// <summary>
/// The chest that the local player has open (K6-30, D9 and D20): subscribes to its container, forgets it when the player closes the
/// panel or the server ends the subscription (the player walked away, the chest became private or was removed), and sends the
/// intents of the panel (take all, put matching, access). Remembers the chests the server refused as locked.
/// </summary>
public sealed class ChestSession : IClientSessionListener
{
    /// <summary>Initializes a new instance of the <see cref="ChestSession"/> class.</summary>
    /// <param name="inventory">The inventory that stores the container.</param>
    /// <param name="sender">Where intents go.</param>
    /// <param name="send">Sends a client message (subscribe, unsubscribe).</param>
    /// <param name="logger">Logger.</param>
    public ChestSession(ClientInventory inventory, IIntentSender sender, Action<Message> send, ILogger<ChestSession> logger)
    {
    }

    /// <summary>Raised when the state of the requested chest arrived and the panel may open.</summary>
    public event Action? Opened;

    /// <summary>Raised when the chest was closed by the player or the server.</summary>
    public event Action? Closed;

    /// <summary>Gets a value indicating whether the state of a chest arrived and the panel is open.</summary>
    public bool IsOpen => throw new NotImplementedException("K6-30 D2");

    /// <summary>Gets a value indicating whether a chest was requested and its state has not arrived.</summary>
    public bool IsOpening => throw new NotImplementedException("K6-30 D2");

    /// <summary>Gets the container of the requested or open chest.</summary>
    public ContainerId Container => throw new NotImplementedException("K6-30 D2");

    /// <summary>Gets the construct of the chest block.</summary>
    public EntityId Construct => throw new NotImplementedException("K6-30 D2");

    /// <summary>Gets the cell of the chest block.</summary>
    public CellPos Cell => throw new NotImplementedException("K6-30 D2");

    /// <summary>Gets the replicated container of the open chest, or null.</summary>
    public ClientContainer? OpenContainer => throw new NotImplementedException("K6-30 D2");

    /// <summary>Gets the reason of the latest refusal of the server for the chest or its panel, <see cref="RejectReason.None"/> after a success.</summary>
    public RejectReason LastRefusal => throw new NotImplementedException("K6-30 D2");

    /// <summary>Gets a value indicating whether the access switch is on offer: unless the server already said the chest is not the player's.</summary>
    public bool CanConfigureAccess => throw new NotImplementedException("K6-30 D2");

    /// <summary>True when the server refused the subscription to a container as locked.</summary>
    /// <param name="container">The container.</param>
    /// <returns>Whether the chest is known to be locked for the player.</returns>
    public bool IsLocked(ContainerId container) => throw new NotImplementedException("K6-30 D2");

    /// <summary>Asks for a chest; closes the chest that is open.</summary>
    /// <param name="container">Container of the chest.</param>
    /// <param name="construct">Construct of the chest block.</param>
    /// <param name="cell">Cell of the chest block.</param>
    public void Open(ContainerId container, EntityId construct, CellPos cell) => throw new NotImplementedException("K6-30 D2");

    /// <summary>Closes the chest and tells the server.</summary>
    public void Close() => throw new NotImplementedException("K6-30 D2");

    /// <summary>Sends the quick stack intent (put matching items into the chests nearby).</summary>
    /// <returns>True when an intent was sent.</returns>
    public bool QuickStack() => throw new NotImplementedException("K6-30 D2");

    /// <summary>Sends the take all intent for the open chest.</summary>
    /// <returns>True when an intent was sent.</returns>
    public bool TakeAll() => throw new NotImplementedException("K6-30 D2");

    /// <summary>Switches the chest between shared and private.</summary>
    /// <returns>True when an intent was sent.</returns>
    public bool ToggleAccess() => throw new NotImplementedException("K6-30 D2");

    /// <inheritdoc />
    public void OnIntentReply(IntentReply reply) => throw new NotImplementedException("K6-30 D2");

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code) => throw new NotImplementedException("K6-30 D2");

    internal void RaiseEvents()
    {
        Opened?.Invoke();
        Closed?.Invoke();
    }
}
