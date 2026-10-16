using Horyzont.Core.Content;
using Horyzont.Core.Crafting;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Progress;
using Horyzont.Core.World.Blocks;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Crafting;

/// <summary>K6-32 D8: a recipe is visible when the world has its key and the player discovered an ingredient (not the fuel).</summary>
public sealed class RecipeBookTests
{
    private static readonly ItemTypeRegistry _registry = WorldFactory.CreateWithPlayerContainers().ItemTypes;
    private static readonly IReadOnlySet<string> _noKeys = new HashSet<string>();

    [Fact]
    public void NoIngredientDiscovered_Unknown()
    {
        // The counterpart: the same recipe with an ingredient discovered is known.
        Assert.True(RecipeBook.IsKnown(Recipe(), _registry, Discovered(CraftingFixture.Stone), _noKeys));

        Assert.False(RecipeBook.IsKnown(Recipe(), _registry, ItemTypeBitSet.Empty, _noKeys));
        Assert.False(RecipeBook.IsKnown(Recipe(), _registry, Discovered(CraftingFixture.Gem), _noKeys));
    }

    [Fact]
    public void OneIngredientDiscovered_Known()
    {
        // Two ingredients, the player has discovered only the second.
        var recipe = Recipe(ingredients: [new RecipeEntry(SurvivalContentFactory.StoneKey, 2), new RecipeEntry(SurvivalContentFactory.ToolKey, 1)]);

        Assert.True(RecipeBook.IsKnown(recipe, _registry, Discovered(CraftingFixture.Tool), _noKeys));
        Assert.True(RecipeBook.IsKnown(recipe, _registry, Discovered(CraftingFixture.Stone), _noKeys));
    }

    [Fact]
    public void OnlyFuelDiscovered_Unknown()
    {
        var recipe = Recipe(fuel: [new RecipeEntry(SurvivalContentFactory.SoilKey, 1)]);
        Assert.True(RecipeBook.IsKnown(recipe, _registry, Discovered(CraftingFixture.Stone), _noKeys));

        Assert.False(RecipeBook.IsKnown(recipe, _registry, Discovered(CraftingFixture.Soil), _noKeys));
    }

    [Fact]
    public void KeyMissing_Unknown()
    {
        var recipe = Recipe(progressKey: "test:boss_2");
        Assert.True(RecipeBook.IsKnown(recipe, _registry, Discovered(CraftingFixture.Stone), new HashSet<string> { "test:boss_2" }));

        Assert.False(RecipeBook.IsKnown(recipe, _registry, Discovered(CraftingFixture.Stone), _noKeys));
        Assert.False(RecipeBook.IsKnown(recipe, _registry, Discovered(CraftingFixture.Stone), new HashSet<string> { "test:boss_1" }));
    }

    [Fact]
    public void KeyPresent_Known()
    {
        var recipe = Recipe(progressKey: "test:boss_2");

        Assert.True(RecipeBook.IsKnown(recipe, _registry, Discovered(CraftingFixture.Stone), new HashSet<string> { "test:other", "test:boss_2" }));

        // A key without a discovered ingredient is not enough.
        Assert.False(RecipeBook.IsKnown(recipe, _registry, ItemTypeBitSet.Empty, new HashSet<string> { "test:boss_2" }));
    }

    [Fact]
    public void IngredientNotInRegistry_Ignored()
    {
        var recipe = Recipe(ingredients: [new RecipeEntry("test:missing", 1), new RecipeEntry(SurvivalContentFactory.StoneKey, 2)]);
        var onlyMissing = Recipe(ingredients: [new RecipeEntry("test:missing", 1)]);

        Assert.True(RecipeBook.IsKnown(recipe, _registry, Discovered(CraftingFixture.Stone), _noKeys));
        Assert.False(RecipeBook.IsKnown(recipe, _registry, ItemTypeBitSet.Empty, _noKeys));
        Assert.False(RecipeBook.IsKnown(onlyMissing, _registry, Discovered(CraftingFixture.Stone), _noKeys));
    }

    [Fact]
    public void IsKnown_UsesAnyItemTypeKeys_NotOnlyTheRegistry()
    {
        // The client resolves keys by the mapping of message 49, which is not the order of the content: the rule has one source (D8).
        var keys = new MappedKeys(SurvivalContentFactory.StoneKey, new ItemTypeId(77));
        Assert.True(RecipeBook.IsKnown(Recipe(), keys, Discovered(new ItemTypeId(77)), _noKeys));
        Assert.False(RecipeBook.IsKnown(Recipe(), keys, Discovered(CraftingFixture.Stone), _noKeys));
    }

    private sealed class MappedKeys(string key, ItemTypeId id) : IItemTypeKeys
    {
        public bool TryGetId(string itemKey, out ItemTypeId found)
        {
            found = id;
            return itemKey == key;
        }
    }

    private static ItemTypeBitSet Discovered(params ItemTypeId[] types) => types.Aggregate(ItemTypeBitSet.Empty, (set, type) => set.With(type));

    private static RecipeDef Recipe(
        RecipeEntry[]? ingredients = null, RecipeEntry[]? fuel = null, string? progressKey = null) => new(
        "t:smelt",
        StationKind.Furnace,
        1,
        ingredients ?? [new RecipeEntry(SurvivalContentFactory.StoneKey, 2)],
        fuel ?? [],
        new RecipeEntry(SurvivalContentFactory.GemKey, 1),
        1,
        progressKey);
}
