using Horyzont.Client.Blocks;
using Horyzont.Client.Inventory;
using Horyzont.Client.Session;
using Horyzont.Client.Tools;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Content;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Blocks;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Crafting;

/// <summary>
/// The station with time (furnace, refinery) that the local player has open (K6-32 D17, D23): subscribes to its input and output containers,
/// forgets them when the player closes the panel or the server ends the subscription, sends the choice of a recipe and take all, and computes the
/// progress bar from the start tick of the replicated state and the tick of the server.
/// </summary>
public sealed class StationSession : IClientSessionListener
{
    /// <summary>Initializes a new instance of the <see cref="StationSession"/> class.</summary>
    /// <param name="inventory">The inventory that stores the containers.</param>
    /// <param name="entities">The states of the functional blocks.</param>
    /// <param name="content">The game content with the recipes.</param>
    /// <param name="sender">Where intents go.</param>
    /// <param name="send">Sends a client message (subscribe, unsubscribe).</param>
    /// <param name="serverTick">The tick of the server.</param>
    /// <param name="rules">Game rules (tick rate).</param>
    /// <param name="logger">Logger.</param>
    public StationSession(
        ClientInventory inventory, IBlockEntityView entities, SurvivalContent content, IIntentSender sender, Action<Message> send,
        ServerTickReader serverTick, GameRules rules, ILogger<StationSession> logger)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(serverTick);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(logger);
    }

    /// <summary>Raised when the states of both containers arrived and the panel may open.</summary>
    public event Action? Opened;

    /// <summary>Raised when the open station was closed by the player or the server (or the session ended).</summary>
    public event Action? Closed;

    /// <summary>Raised whenever something the panel shows changed.</summary>
    public event Action? Changed;

    /// <summary>Gets a value indicating whether both containers arrived and the panel is open.</summary>
    public bool IsOpen => throw new NotImplementedException("K6-32 D2");

    /// <summary>Gets a value indicating whether a station was requested and a container has not arrived.</summary>
    public bool IsOpening => throw new NotImplementedException("K6-32 D2");

    /// <summary>Gets the kind of the requested or open station.</summary>
    public StationKind Kind => throw new NotImplementedException("K6-32 D2");

    /// <summary>Gets the requested or open station block, null when closed.</summary>
    public BlockRef? Station => throw new NotImplementedException("K6-32 D2");

    /// <summary>Gets the replicated input container of the open station, or null.</summary>
    public ClientContainer? Input => throw new NotImplementedException("K6-32 D2");

    /// <summary>Gets the replicated output container of the open station, or null.</summary>
    public ClientContainer? Output => throw new NotImplementedException("K6-32 D2");

    /// <summary>Gets the recipe the station works on, null when none is chosen.</summary>
    public RecipeDef? ActiveRecipe => throw new NotImplementedException("K6-32 D2");

    /// <summary>Gets the part of the current piece done, 0 to 1, or null when the station is idle or the tick of the server is unknown.</summary>
    public double? Progress => throw new NotImplementedException("K6-32 D2");

    /// <summary>Gets the reason of the latest refusal, <see cref="RejectReason.None"/> after a success.</summary>
    public RejectReason LastRefusal => throw new NotImplementedException("K6-32 D2");

    /// <summary>Asks for the containers of a station; closes the station that is open.</summary>
    /// <param name="kind">Station kind.</param>
    /// <param name="station">The station block.</param>
    /// <returns>False when the state of the block is not replicated yet (nothing was sent).</returns>
    public bool Open(StationKind kind, BlockRef station)
    {
        Opened?.Invoke();
        Closed?.Invoke();
        Changed?.Invoke();
        throw new NotImplementedException("K6-32 D2");
    }

    /// <summary>Closes the station and tells the server.</summary>
    public void Close() => throw new NotImplementedException("K6-32 D2");

    /// <summary>Chooses the recipe of the station.</summary>
    /// <param name="recipe">A recipe of the station kind.</param>
    /// <returns>True when an intent was sent.</returns>
    public bool SelectRecipe(RecipeDef recipe) => throw new NotImplementedException("K6-32 D2");

    /// <summary>Takes everything from the output container.</summary>
    /// <returns>True when an intent was sent.</returns>
    public bool TakeAll() => throw new NotImplementedException("K6-32 D2");

    /// <inheritdoc />
    public void OnIntentReply(IntentReply reply) => throw new NotImplementedException("K6-32 D2");

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code) => throw new NotImplementedException("K6-32 D2");
}
