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
/// Crafting of the local player (K6-32 D12): the recipes of the hands by default, the recipes of a station with no time (workbench, kitchen,
/// fabricator) while the player has one open. Sends <see cref="CraftIntent"/>s and keeps the latest refusal; nothing is predicted (D23).
/// </summary>
public sealed class CraftingSession : IClientSessionListener
{
    /// <summary>The number of craft intents that may wait for the server.</summary>
    public const int MaxPending = 4;

    /// <summary>Initializes a new instance of the <see cref="CraftingSession"/> class.</summary>
    /// <param name="sender">Where intents go.</param>
    /// <param name="rules">Game rules (the largest count).</param>
    /// <param name="logger">Logger.</param>
    public CraftingSession(IIntentSender sender, GameRules rules, ILogger<CraftingSession> logger)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(logger);
    }

    /// <summary>Raised when a station was opened.</summary>
    public event Action? Opened;

    /// <summary>Raised when the open station was closed.</summary>
    public event Action? Closed;

    /// <summary>Raised whenever something the panel shows changed.</summary>
    public event Action? Changed;

    /// <summary>Gets the station kind whose recipes are on offer, <see cref="StationKind.Hands"/> without a station.</summary>
    public StationKind Kind => throw new NotImplementedException("K6-32 D2");

    /// <summary>Gets the open station block, null for the hands.</summary>
    public BlockRef? Station => throw new NotImplementedException("K6-32 D2");

    /// <summary>Gets a value indicating whether a station is open.</summary>
    public bool IsStationOpen => throw new NotImplementedException("K6-32 D2");

    /// <summary>Gets the number of craft intents that wait for the server.</summary>
    public int PendingCount => throw new NotImplementedException("K6-32 D2");

    /// <summary>Gets the reason of the latest refusal, <see cref="RejectReason.None"/> after a success.</summary>
    public RejectReason LastRefusal => throw new NotImplementedException("K6-32 D2");

    /// <summary>Gets a value indicating whether the server refused the latest craft as an unknown recipe.</summary>
    public bool ShowsLocked => LastRefusal == RejectReason.RecipeLocked;

    /// <summary>Opens a station with no time.</summary>
    /// <param name="kind">Station kind.</param>
    /// <param name="station">The station block.</param>
    public void OpenStation(StationKind kind, BlockRef station)
    {
        Opened?.Invoke();
        Closed?.Invoke();
        Changed?.Invoke();
        throw new NotImplementedException("K6-32 D2");
    }

    /// <summary>Closes the station; the recipes of the hands are on offer again.</summary>
    public void Close() => throw new NotImplementedException("K6-32 D2");

    /// <summary>Asks the server to craft a recipe.</summary>
    /// <param name="recipe">The recipe; its station kind must be the open one.</param>
    /// <param name="count">Pieces, 1 to <see cref="GameRules.MaxCraftCount"/>.</param>
    /// <returns>What happened.</returns>
    public CraftOutcome Craft(RecipeDef recipe, ushort count) => throw new NotImplementedException("K6-32 D2");

    /// <inheritdoc />
    public void OnIntentReply(IntentReply reply) => throw new NotImplementedException("K6-32 D2");

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code) => throw new NotImplementedException("K6-32 D2");
}
