using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Content;
using Horyzont.Core.Crafting;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Resolution of crafting and the stations with time (K6-32 D12 to D14, D17, D18): <see cref="CraftIntent"/>, putting up and taking down a
/// station with time, the recipe choice. Called by <see cref="ServerSimulation"/> before the functional block rules; the order of the checks is in
/// docs/plans/K6-32-rzemioslo-i-stacje.md, "Walidacja i rozwiązanie intencji". Reads the content in the resolver only (ZASADY §4): every value the
/// operations depend on (stack limits, counts, container ids and sizes, the start tick) is carried by the operations.
/// </summary>
internal sealed class CraftingRules(WorldState state, GameRules rules, BlockContentTable content, SurvivalContent? survival)
{
    private readonly List<Operation> _components = [];
    private readonly List<Source> _sources = [];

    /// <summary>Resolves the intents this class owns.</summary>
    /// <param name="player">Acting player.</param>
    /// <param name="intent">The validated intent.</param>
    /// <param name="facts">Facts of the validator.</param>
    /// <param name="operation">The operation when <paramref name="reason"/> is <see cref="RejectReason.None"/>.</param>
    /// <param name="reason">The refusal, or none.</param>
    /// <returns>False when the intent is not a crafting intent (the caller resolves it otherwise).</returns>
    internal bool TryResolve(PlayerId player, Intent intent, IntentFacts facts, out Operation operation, out RejectReason reason)
    {
        operation = null!;
        reason = RejectReason.None;
        switch (intent)
        {
            case CraftIntent craft:
                reason = ResolveCraft(player, craft, out operation);
                return true;

            case PlaceBlockIntent place:
                var function = content.Get(place.BlockTypeId).Function;
                if (function.Kind != BlockFunctionKind.Station || !StationKinds.IsTimed(function.Station))
                {
                    return false;
                }

                reason = ResolvePlaceStation(player, place, facts, function, out operation);
                return true;

            case RemoveBlockIntent remove:
                if (!state.BlockEntities.TryGet(new BlockRef(remove.Construct, remove.Cell), out var entity) || entity is not StationBlockState station)
                {
                    return false;
                }

                reason = ResolveRemoveStation(remove, station, out operation);
                return true;

            default:
                return false;
        }
    }

    /// <summary>True when the content has recipes (the crafting and the stations with time are on).</summary>
    internal bool HasRecipes => survival is { Recipes.Count: > 0 };

    private RejectReason ResolvePlaceStation(PlayerId player, PlaceBlockIntent place, IntentFacts facts, BlockFunction function, out Operation operation)
    {
        operation = null!;
        if (!HasRecipes || function.ContainerSlots is 0 or > ProtocolConstants.MaxContainerSlots || function.OutputSlots is 0 or > ProtocolConstants.MaxContainerSlots)
        {
            return RejectReason.InvalidParameters;
        }

        var block = IntentResolver.Resolve(state, player, place, facts);
        EntityId construct;
        CellPos cell;
        switch (block)
        {
            case BlockSetOp set:
                construct = set.Construct;
                cell = set.Cell;
                break;
            case ConstructCreateOp create:
                construct = create.Construct;
                cell = create.FirstCell;
                break;
            default:
                throw new InvalidOperationException($"A block placement resolved to {block.Kind}.");
        }

        // The second id is the first plus one: the applier allocates them in this order (D18).
        var input = state.ContainerIds.PeekNext();
        var output = new ContainerId(input.Value + 1);
        var host = ContainerHost.ConstructBlock(construct, cell);
        operation = new CompoundOp(
        [
            new ContainerCreateOp(input, ContainerKind.StationInput, host, player, ContainerAccess.Shared, function.ContainerSlots),
            new ContainerCreateOp(output, ContainerKind.StationOutput, host, player, ContainerAccess.Shared, function.OutputSlots),
            new BlockEntitySetOp(construct, cell, place.BlockTypeId, new StationBlockState(input, output, string.Empty, null)),
            block,
        ]);
        return RejectReason.None;
    }

    private RejectReason ResolveRemoveStation(RemoveBlockIntent remove, StationBlockState station, out Operation operation)
    {
        operation = null!;
        if (!HasRecipes || !state.Constructs.TryGet(remove.Construct, out var construct)
            || !state.Containers.TryGet(station.Input, out var input) || !state.Containers.TryGet(station.Output, out var output))
        {
            return RejectReason.InvalidParameters;
        }

        if (!IsEmpty(input) || !IsEmpty(output))
        {
            return RejectReason.ContainerNotEmpty;
        }

        operation = new CompoundOp(
        [
            new ContainerRemoveOp(input.Id),
            new ContainerRemoveOp(output.Id),
            new BlockEntitySetOp(remove.Construct, remove.Cell, construct.GetBlock(remove.Cell).TypeId, null),
            new BlockClearOp(remove.Construct, remove.Cell),
        ]);
        return RejectReason.None;
    }

    private RejectReason ResolveCraft(PlayerId player, CraftIntent craft, out Operation operation)
    {
        operation = null!;
        if (survival is null || !HasRecipes || !survival.TryGetRecipe(craft.Recipe, out var recipe))
        {
            return RejectReason.InvalidParameters;
        }

        var discovered = state.Progress.TryGet(player, out var progress) ? progress.DiscoveredItems : ItemTypeBitSet.Empty;
        if (!RecipeBook.IsKnown(recipe, state.ItemTypes, discovered, state.ProgressKeys.AsSet))
        {
            return RejectReason.RecipeLocked;
        }

        if (recipe.Station == StationKind.Hands)
        {
            if (craft.Station is not null)
            {
                return RejectReason.InvalidParameters;
            }

            return ResolveInstant(player, craft, recipe, discovered, station: null, out operation);
        }

        if (craft.Station is not { } target)
        {
            return RejectReason.StationRequired;
        }

        var reason = CheckStation(player, recipe, target, out var typeId);
        if (reason != RejectReason.None)
        {
            return reason;
        }

        return StationKinds.IsTimed(recipe.Station)
            ? ResolveSelect(craft, recipe, target, typeId, out operation)
            : ResolveInstant(player, craft, recipe, discovered, target, out operation);
    }

    /// <summary>The station of a craft intent, in the order of D14: it exists, has the kind of the recipe, is within reach, has the level.</summary>
    private RejectReason CheckStation(PlayerId player, RecipeDef recipe, BlockRef target, out ushort typeId)
    {
        typeId = 0;
        if (!state.Constructs.TryGet(target.Construct, out var construct))
        {
            return RejectReason.UnknownConstruct;
        }

        if (!target.Cell.IsInBounds)
        {
            return RejectReason.CellOutOfBounds;
        }

        var block = construct.GetBlock(target.Cell);
        if (block.IsEmpty)
        {
            return RejectReason.CellEmpty;
        }

        var function = content.Get(block.TypeId).Function;
        if (function.Kind != BlockFunctionKind.Station || function.Station != recipe.Station)
        {
            return RejectReason.StationRequired;
        }

        if (!ContainerReach.TryDistanceFromEye(state, rules, player, target.Construct, target.Cell, out var meters) || meters > rules.StationUseRangeMeters)
        {
            return RejectReason.TooFarFromStation;
        }

        if (StationLevels.Compute(state, rules, content, target.Construct, target.Cell) < recipe.Level)
        {
            return RejectReason.StationRequired;
        }

        typeId = block.TypeId;
        return RejectReason.None;
    }

    /// <summary>Choosing the recipe of a station with time (D18): the state with the new recipe, started now when a piece can be made.</summary>
    private RejectReason ResolveSelect(CraftIntent craft, RecipeDef recipe, BlockRef target, ushort typeId, out Operation operation)
    {
        operation = null!;
        if (craft.Count != 1 || !state.BlockEntities.TryGet(target, out var entity) || entity is not StationBlockState station
            || string.Equals(station.Recipe, recipe.Key, StringComparison.Ordinal)
            || !state.Containers.TryGet(station.Input, out var input) || !state.Containers.TryGet(station.Output, out var output)
            || !StationSettlements.TryTranslate(state, survival!, recipe, out var translated, out var limit))
        {
            return RejectReason.InvalidParameters;
        }

        // A piece can be made when the input holds one piece and the output has room for its result; a new recipe always starts from "idle".
        var started = StationProcess.Settle(input.Slots, output.Slots, translated, 1, null, state.CurrentTick, limit).NextStarted;
        operation = new BlockEntitySetOp(target.Construct, target.Cell, typeId, station with { Recipe = recipe.Key, Started = started });
        return RejectReason.None;
    }

    /// <summary>Crafting in the hands or at a station without time (D12): the destroys of the ingredients, the creates of the result in the bag, the discovery.</summary>
    private RejectReason ResolveInstant(PlayerId player, CraftIntent craft, RecipeDef recipe, ItemTypeBitSet discovered, BlockRef? station, out Operation operation)
    {
        operation = null!;
        if (!state.Containers.TryGetOfPlayer(player, ContainerKind.PlayerBag, out var bag)
            || !state.Containers.TryGetOfPlayer(player, ContainerKind.Hotbar, out var hotbar)
            || !state.ItemTypes.TryGetId(recipe.Result.ItemKey, out var resultType)
            || !survival!.Items.TryGet(recipe.Result.ItemKey, out var resultDef))
        {
            return RejectReason.InvalidParameters;
        }

        BuildSources(player, station, bag, hotbar);
        _components.Clear();
        var bagIndex = 0;
        for (var i = 0; i < recipe.Ingredients.Count; i++)
        {
            var entry = recipe.Ingredients[i];
            if (!state.ItemTypes.TryGetId(entry.ItemKey, out var type))
            {
                return RejectReason.InvalidParameters;
            }

            var need = entry.Count * craft.Count;
            var have = 0;
            for (var s = 0; s < _sources.Count; s++)
            {
                have += ItemPlacement.CountOf(_sources[s].Slots, type);
            }

            if (have < need)
            {
                return RejectReason.NotEnoughItems;
            }

            for (var s = 0; s < _sources.Count && need > 0; s++)
            {
                need = ItemPlacement.Destroy(_sources[s].Id, _sources[s].Slots, type, need, ItemSink.Craft, _components);
            }
        }

        // The room is counted on the copy of the bag after the ingredients were taken (D12); the result goes to the bag only.
        var wanted = recipe.Result.Count * craft.Count;
        var made = ItemPlacement.CreateInContainer(bag.Id, _sources[bagIndex].Slots, resultType, wanted, resultDef.MaxStack, ItemSource.Craft, _components);
        if (made < wanted)
        {
            return RejectReason.InventoryFull;
        }

        if (!discovered.Contains(resultType))
        {
            _components.Add(new ItemDiscoverOp(player, resultType));
        }

        if (_components.Count > ProtocolConstants.MaxCompoundComponents)
        {
            return RejectReason.InvalidParameters;
        }

        operation = new CompoundOp(_components.ToArray());
        return RejectReason.None;
    }

    /// <summary>The sources of the ingredients (D13): the bag, the hotbar, then the chests the player may use within the range of the centre, nearest first.</summary>
    private void BuildSources(PlayerId player, BlockRef? station, Container bag, Container hotbar)
    {
        _sources.Clear();
        _sources.Add(new Source(bag.Id, bag.Slots.ToArray(), 0f));
        _sources.Add(new Source(hotbar.Id, hotbar.Slots.ToArray(), 0f));
        var chests = state.Containers.OfKind(ContainerKind.Chest);
        if (chests.Count == 0)
        {
            return;
        }

        PlanetId planet;
        Vector3 centre;
        if (station is { } at)
        {
            if (!ContainerReach.TryCellCenter(state, at.Construct, at.Cell, out planet, out centre))
            {
                return;
            }
        }
        else if (!state.Players.TryGet(player, out var playerState) || !ContainerReach.TryGetEye(state, rules, playerState, out planet, out centre))
        {
            return;
        }

        var first = _sources.Count;
        for (var i = 0; i < chests.Count; i++)
        {
            var chest = chests[i];
            if (chest.Host.Kind != ContainerHostKind.ConstructBlock || (chest.Access != ContainerAccess.Shared && chest.Owner != player)
                || !ContainerReach.TryCellCenter(state, new EntityId(chest.Host.Id), chest.Host.Cell, out var chestPlanet, out var chestCentre)
                || chestPlanet != planet)
            {
                continue;
            }

            var meters = Vector3.Distance(centre, chestCentre);
            if (meters <= rules.CraftContainerRangeMeters)
            {
                _sources.Add(new Source(chest.Id, chest.Slots.ToArray(), meters));
            }
        }

        _sources.Sort(first, _sources.Count - first, SourceOrder.Instance);
    }

    private static bool IsEmpty(Container container)
    {
        for (var slot = 0; slot < container.Size; slot++)
        {
            if (!container.GetSlot(slot).IsEmpty)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A container the ingredients may come from, with a copy of its slots that the plan consumes.</summary>
    private readonly record struct Source(ContainerId Id, ItemStack[] Slots, float Meters);

    private sealed class SourceOrder : IComparer<Source>
    {
        internal static readonly SourceOrder Instance = new();

        public int Compare(Source x, Source y)
        {
            var byDistance = x.Meters.CompareTo(y.Meters);
            return byDistance != 0 ? byDistance : x.Id.Value.CompareTo(y.Id.Value);
        }
    }
}
