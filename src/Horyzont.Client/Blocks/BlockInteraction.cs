using Horyzont.Client.Inventory;
using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Client.Blocks;

/// <summary>
/// The interaction key of the local player (K6-30): looks at the block under the crosshair within reach and either sends a
/// <c>BlockInteractIntent</c> (doors, lamps, beds) or asks <see cref="ChestSession"/> to open a chest. Also keeps <see cref="Prompt"/>
/// for the hint on the screen. Nothing changes on the client before the server answers (decision D24).
/// </summary>
public sealed class BlockInteraction
{
    /// <summary>Initializes a new instance of the <see cref="BlockInteraction"/> class.</summary>
    /// <param name="world">World view of the tools.</param>
    /// <param name="sender">Where intents go.</param>
    /// <param name="pending">Tracker that settles the replies.</param>
    /// <param name="content">Block definitions of the client content, indexed by the ids of the server registry.</param>
    /// <param name="entities">The states of the functional blocks.</param>
    /// <param name="chests">The chest session.</param>
    /// <param name="localPlayer">The local player (the owner of a bed is compared with it).</param>
    /// <param name="rules">Game rules (reach).</param>
    public BlockInteraction(
        IToolWorldView world, IIntentSender sender, PendingIntentTracker pending, BlockContentTable content, ClientBlockEntities entities,
        ChestSession chests, PlayerId localPlayer, GameRules rules)
    {
    }

    /// <summary>Gets the hint for the block under the crosshair at the latest <see cref="Observe"/>, or null.</summary>
    public BlockPrompt? Prompt => throw new NotImplementedException("K6-30 D2");

    /// <summary>The aim is not usable this tick: nothing is aimed at.</summary>
    public void ClearAim() => throw new NotImplementedException("K6-30 D2");

    /// <summary>Looks at the crosshair; call at a low rate.</summary>
    /// <param name="aim">Aim of the local player.</param>
    public void Observe(in AimRay aim) => throw new NotImplementedException("K6-30 D2");

    /// <summary>Handles a press of the interaction key.</summary>
    /// <param name="aim">Aim of the local player.</param>
    /// <param name="clientTick">Client physics tick.</param>
    /// <returns>What happened.</returns>
    public BlockInteractionResult Interact(in AimRay aim, long clientTick) => throw new NotImplementedException("K6-30 D2");
}
