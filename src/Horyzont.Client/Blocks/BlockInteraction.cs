using Horyzont.Client.Crafting;
using Horyzont.Client.Inventory;
using Horyzont.Client.Tools;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Crafting;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Blocks;

/// <summary>
/// The interaction key of the local player (K6-30): looks at the block under the crosshair within reach and either sends a
/// <see cref="BlockInteractIntent"/> (doors, lamps, beds) or asks <see cref="ChestSession"/> to open a chest. Also keeps
/// <see cref="Prompt"/> for the hint on the screen. Nothing changes on the client before the server answers (decision D24); the ray
/// starts at the eye like the one of the tools (<see cref="AimRay.Eye"/>, the server formula).
/// </summary>
public sealed class BlockInteraction
{
    private readonly IToolWorldView _world;
    private readonly IIntentSender _sender;
    private readonly PendingIntentTracker _pending;
    private readonly BlockContentTable _content;
    private readonly IBlockEntityView _entities;
    private readonly ChestSession _chests;
    private readonly PlayerId _localPlayer;
    private readonly GameRules _rules;
    private readonly CraftingSession? _crafting;
    private readonly StationSession? _stations;

    /// <summary>Initializes a new instance of the <see cref="BlockInteraction"/> class.</summary>
    /// <param name="world">World view of the tools.</param>
    /// <param name="sender">Where intents go.</param>
    /// <param name="pending">Tracker that settles the replies.</param>
    /// <param name="content">Block definitions of the client content, indexed by the ids of the server registry.</param>
    /// <param name="entities">The states of the functional blocks.</param>
    /// <param name="chests">The chest session.</param>
    /// <param name="localPlayer">The local player (the owner of a bed is compared with it).</param>
    /// <param name="rules">Game rules (reach).</param>
    /// <param name="crafting">The crafting session that a station with no time opens (K6-32); null: such stations give no prompt.</param>
    /// <param name="stations">The station session that a station with time opens (K6-32); null: such stations give no prompt.</param>
    public BlockInteraction(
        IToolWorldView world, IIntentSender sender, PendingIntentTracker pending, BlockContentTable content, IBlockEntityView entities,
        ChestSession chests, PlayerId localPlayer, GameRules rules, CraftingSession? crafting = null, StationSession? stations = null)
    {
        _crafting = crafting;
        _stations = stations;
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _chests = chests ?? throw new ArgumentNullException(nameof(chests));
        _localPlayer = localPlayer;
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    /// <summary>Gets the hint for the block under the crosshair at the latest <see cref="Observe"/>, or null.</summary>
    public BlockPrompt? Prompt { get; private set; }

    /// <summary>The aim is not usable this tick: nothing is aimed at.</summary>
    public void ClearAim() => Prompt = null;

    /// <summary>Looks at the crosshair; call at a low rate.</summary>
    /// <param name="aim">Aim of the local player.</param>
    public void Observe(in AimRay aim) => Prompt = Find(aim);

    /// <summary>Handles a press of the interaction key.</summary>
    /// <param name="aim">Aim of the local player.</param>
    /// <param name="clientTick">Client physics tick.</param>
    /// <returns>What happened.</returns>
    public BlockInteractionResult Interact(in AimRay aim, long clientTick)
    {
        if (Find(aim) is not { } prompt)
        {
            return BlockInteractionResult.NothingToInteract;
        }

        switch (prompt.Kind)
        {
            case BlockPromptKind.OpenChest:
            case BlockPromptKind.ChestLocked:
                // A locked chest is asked again: the owner may have made it shared since the refusal.
                if (_entities.TryGet(prompt.Construct, prompt.Cell, out var state) && state is ChestBlockState chest)
                {
                    _chests.Open(chest.Container, prompt.Construct, prompt.Cell);
                    return BlockInteractionResult.ChestRequested;
                }

                return BlockInteractionResult.NothingToInteract;
            case BlockPromptKind.OpenGenerator:
                if (_entities.TryGet(prompt.Construct, prompt.Cell, out var generatorState) && generatorState is OxygenGeneratorBlockState generator)
                {
                    _chests.Open(generator.Input, prompt.Construct, prompt.Cell);
                    return BlockInteractionResult.ChestRequested;
                }

                return BlockInteractionResult.NothingToInteract;
            case BlockPromptKind.OpenStation:
                var station = new BlockRef(prompt.Construct, prompt.Cell);
                if (StationKinds.IsTimed(prompt.Station))
                {
                    return _stations is not null && _stations.Open(prompt.Station, station)
                        ? BlockInteractionResult.StationRequested
                        : BlockInteractionResult.NothingToInteract;
                }

                if (_crafting is null)
                {
                    return BlockInteractionResult.NothingToInteract;
                }

                _crafting.OpenStation(prompt.Station, station);
                return BlockInteractionResult.CraftingOpened;
            case BlockPromptKind.OwnBed:
            case BlockPromptKind.BedOccupied:
                return BlockInteractionResult.Refused;
            default:
                if (!_sender.CanSend)
                {
                    return BlockInteractionResult.CannotSend;
                }

                if (_pending.IsFull)
                {
                    return BlockInteractionResult.Waiting;
                }

                var action = prompt.Kind == BlockPromptKind.Sleep ? BlockInteractAction.Sleep : BlockInteractAction.Toggle;
                var intent = new BlockInteractIntent(prompt.Construct, prompt.Cell, action);
                _pending.Add(_sender.Submit(intent), intent, default, clientTick);
                return BlockInteractionResult.Sent;
        }
    }

    private BlockPrompt? Find(in AimRay aim)
    {
        if (!_world.RaycastConstructs(aim.Planet, aim.Eye, aim.Direction, _rules.ReachMeters, out var hit)
            || !_world.TryGetConstruct(hit.Construct, out var construct))
        {
            return null;
        }

        var block = construct.GetBlock(hit.Cell);
        var function = _content.Get(block.TypeId).Function;
        var kind = function.Kind switch
        {
            BlockFunctionKind.Station => StationPrompt(construct.Id, hit.Cell, function.Station),
            BlockFunctionKind.Chest => ChestPrompt(construct.Id, hit.Cell),
            BlockFunctionKind.OxygenGenerator => GeneratorPrompt(construct.Id, hit.Cell),
            BlockFunctionKind.Bed => BedPrompt(construct.Id, hit.Cell),
            BlockFunctionKind.Door => block.FunctionState == 0 ? BlockPromptKind.OpenDoor : BlockPromptKind.CloseDoor,
            BlockFunctionKind.Light => block.FunctionState == 0 ? BlockPromptKind.SwitchLightOn : BlockPromptKind.SwitchLightOff,
            _ => (BlockPromptKind?)null,
        };
        return kind is { } found
            ? new BlockPrompt(found, construct.Id, hit.Cell, found == BlockPromptKind.OpenStation ? function.Station : StationKind.Hands)
            : null;
    }

    private BlockPromptKind? StationPrompt(EntityId construct, CellPos cell, StationKind station)
    {
        if (!StationKinds.IsTimed(station))
        {
            return _crafting is null ? null : BlockPromptKind.OpenStation;
        }

        // Like a chest: without the replicated state the containers of the station are unknown.
        return _stations is not null && _entities.TryGet(construct, cell, out var state) && state is StationBlockState
            ? BlockPromptKind.OpenStation
            : null;
    }

    // Like a chest: without the replicated state the container of the input is unknown.
    private BlockPromptKind? GeneratorPrompt(EntityId construct, CellPos cell) =>
        _entities.TryGet(construct, cell, out var state) && state is OxygenGeneratorBlockState ? BlockPromptKind.OpenGenerator : null;

    private BlockPromptKind? ChestPrompt(EntityId construct, CellPos cell)
    {
        // Without the replicated state the container of the chest is unknown: the chest cannot be opened yet.
        if (!_entities.TryGet(construct, cell, out var state) || state is not ChestBlockState chest)
        {
            return null;
        }

        return _chests.IsLocked(chest.Container) ? BlockPromptKind.ChestLocked : BlockPromptKind.OpenChest;
    }

    private BlockPromptKind? BedPrompt(EntityId construct, CellPos cell)
    {
        if (!_entities.TryGet(construct, cell, out var state) || state is not BedBlockState bed)
        {
            return null;
        }

        if (bed.Owner.IsNone)
        {
            return BlockPromptKind.Sleep;
        }

        return bed.Owner == _localPlayer ? BlockPromptKind.OwnBed : BlockPromptKind.BedOccupied;
    }
}
