using Horyzont.Core.BlockEntities;
using Horyzont.Core.Content;
using Horyzont.Core.Crafting;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.World;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Builds the settlement of a station with time as one operation (K6-32 D16): a compound of the destroys of the input (the ingredients in the order of
/// the recipe, then the fuel), the creates in the output and the new block state, in that order; with no piece done, the new state alone. Reads the
/// content (the recipe, the time, the stack limit of the result) in the resolver only; the operation carries every value.
/// </summary>
internal sealed class StationSettlements(WorldState state, GameRules rules, SurvivalContent survival)
{
    private readonly List<Operation> _components = [];

    /// <summary>Translates a recipe to the item type ids of a world and finds the stack limit of its result.</summary>
    /// <param name="state">The world (the item types).</param>
    /// <param name="survival">Survival content (the item definitions).</param>
    /// <param name="recipe">The recipe.</param>
    /// <param name="translated">The translated recipe when the method returns true.</param>
    /// <param name="resultLimit">Stack limit of the result.</param>
    /// <returns>False when an item of the recipe is not in the world.</returns>
    internal static bool TryTranslate(WorldState state, SurvivalContent survival, RecipeDef recipe, out StationRecipe translated, out ushort resultLimit)
    {
        translated = default;
        resultLimit = 0;
        if (!TryEntries(state, recipe.Ingredients, out var ingredients) || !TryEntries(state, recipe.Fuel, out var fuel)
            || !state.ItemTypes.TryGetId(recipe.Result.ItemKey, out var resultType) || !survival.Items.TryGet(recipe.Result.ItemKey, out var def))
        {
            return false;
        }

        translated = new StationRecipe(ingredients, fuel, new StationEntry(resultType, recipe.Result.Count));
        resultLimit = def.MaxStack;
        return true;
    }

    /// <summary>Builds the settlement of <paramref name="station"/> at tick <paramref name="now"/>.</summary>
    /// <param name="station">The block with the station state.</param>
    /// <param name="now">The tick to settle at.</param>
    /// <param name="operation">The operation when the method returns true.</param>
    /// <returns>False when nothing changes (no state, no recipe, no piece done and the same start), so no operation is made.</returns>
    internal bool TryBuild(BlockRef station, Tick now, out Operation operation)
    {
        operation = null!;
        if (!state.BlockEntities.TryGet(station, out var entity) || entity is not StationBlockState current || current.Recipe.Length == 0
            || !survival.TryGetRecipe(current.Recipe, out var recipe) || !state.Constructs.TryGet(station.Construct, out var construct)
            || !state.Containers.TryGet(current.Input, out var input) || !state.Containers.TryGet(current.Output, out var output)
            || !TryTranslate(state, survival, recipe, out var translated, out var limit))
        {
            return false;
        }

        var duration = Math.Max(1UL, (ulong)recipe.TimeSeconds * (ulong)rules.TickRate);
        var settled = StationProcess.Settle(input.Slots, output.Slots, translated, duration, current.Started, now, limit);
        if (settled.Done == 0 && settled.NextStarted == current.Started)
        {
            return false;
        }

        var type = construct.GetBlock(station.Cell).TypeId;
        var next = current with { Started = settled.NextStarted };
        if (settled.Done == 0)
        {
            operation = new BlockEntitySetOp(station.Construct, station.Cell, type, next);
            return true;
        }

        var inputSlots = input.Slots.ToArray();
        var outputSlots = output.Slots.ToArray();
        _components.Clear();
        for (var i = 0; i < translated.Ingredients.Count; i++)
        {
            TakeFromInput(input.Id, inputSlots, translated.Ingredients[i], settled.Done, ItemSink.Station);
        }

        for (var i = 0; i < translated.Fuel.Count; i++)
        {
            TakeFromInput(input.Id, inputSlots, translated.Fuel[i], settled.Done, ItemSink.Fuel);
        }

        ItemPlacement.CreateInContainer(
            output.Id, outputSlots, translated.Result.Type, translated.Result.Count * settled.Done, limit, ItemSource.Station, _components);
        _components.Add(new BlockEntitySetOp(station.Construct, station.Cell, type, next));
        operation = new CompoundOp(_components.ToArray());
        return true;
    }

    private static bool TryEntries(WorldState state, IReadOnlyList<RecipeEntry> entries, out StationEntry[] result)
    {
        result = new StationEntry[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            if (!state.ItemTypes.TryGetId(entries[i].ItemKey, out var type))
            {
                return false;
            }

            result[i] = new StationEntry(type, entries[i].Count);
        }

        return true;
    }

    private void TakeFromInput(ContainerId input, ItemStack[] slots, StationEntry entry, int pieces, ItemSink sink) =>
        ItemPlacement.Destroy(input, slots, entry.Type, entry.Count * pieces, sink, _components);
}
