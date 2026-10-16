using Horyzont.Core.Content;
using Horyzont.Core.World.Blocks;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Content;

/// <summary>
/// <c>content/recipes/recipes.json</c> (K6-32 D4): the file is optional, every error is an <see cref="InvalidDataException"/> naming the file and the JSON
/// path, and the result of a recipe is a source of the item. Until step B3 the recipes overload of <see cref="ContentLoader.Parse(string, string, string, string?, string?)"/>
/// is a skeleton, so these tests fail on it. Every refusal test first parses the valid recipe it mutates.
/// </summary>
public sealed class RecipeContentTests
{
    private const string Items = """
        { "items": [
          { "key": "test:tool", "category": "tool", "flags": ["tool"], "toolTier": 1 },
          { "key": "test:stone", "category": "resource", "flags": ["bulk"] },
          { "key": "test:ore", "category": "resource", "flags": ["bulk"] },
          { "key": "test:peat", "category": "resource", "flags": ["bulk"] },
          { "key": "test:ingot", "category": "component", "maxStack": 5, "maxStackOverride": true },
          { "key": "test:old", "category": "resource", "flags": ["retired"] } ] }
        """;

    private const string Yields = """
        { "yields": [
          { "material": "stone", "item": "test:stone", "itemsPerCubicMeter": 2, "toolTier": 1 },
          { "material": "ore", "item": "test:ore", "itemsPerCubicMeter": 1, "toolTier": 1 },
          { "material": "peat", "item": "test:peat", "itemsPerCubicMeter": 1, "toolTier": 1 } ] }
        """;

    private const string Start = """
        { "containers": { "playerBag": 40, "hotbar": 10, "equipment": 7 },
          "items": [ { "item": "test:tool", "count": 1, "container": "hotbar", "slot": 0 } ] }
        """;

    private const string Smelt = """
        { "key": "t:smelt", "station": "furnace", "level": 1, "progressKey": null,
          "ingredients": [ { "item": "test:ore", "count": 2 } ], "fuel": [ { "item": "test:peat", "count": 1 } ],
          "result": { "item": "test:ingot", "count": 1 }, "timeSeconds": 20 }
        """;

    private const string Hand = """
        { "key": "t:hand", "station": "hands", "level": 1,
          "ingredients": [ { "item": "test:stone", "count": 2 } ], "result": { "item": "test:ingot", "count": 1 } }
        """;

    private static string Recipes(params string[] entries) => "{ \"formatVersion\": 1, \"recipes\": [ " + string.Join(", ", entries) + " ] }";

    private static SurvivalContent Parse(params string[] entries) => ContentLoader.Parse(Items, Yields, Start, null, Recipes(entries));

    private static void AssertInvalid(string path, params string[] entries)
    {
        var exception = Assert.Throws<InvalidDataException>(() => Parse(entries));
        Assert.Contains(ContentLoader.RecipesFile, exception.Message, StringComparison.Ordinal);
        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ValidRecipes_AreRead()
    {
        var content = Parse(Smelt, Hand);

        Assert.Equal(["t:smelt", "t:hand"], content.Recipes.Select(r => r.Key));
        Assert.True(content.TryGetRecipe("t:smelt", out var smelt));
        Assert.Equal((StationKind.Furnace, (byte)1, 20u, (string?)null), (smelt.Station, smelt.Level, smelt.TimeSeconds, smelt.ProgressKey));
        Assert.Equal([new RecipeEntry("test:ore", 2)], smelt.Ingredients);
        Assert.Equal([new RecipeEntry("test:peat", 1)], smelt.Fuel);
        Assert.Equal(new RecipeEntry("test:ingot", 1), smelt.Result);
        Assert.True(content.TryGetRecipe("t:hand", out var hand));
        Assert.Equal((StationKind.Hands, 0u, 0), (hand.Station, hand.TimeSeconds, hand.Fuel.Count));
        Assert.False(content.TryGetRecipe("t:nope", out _));
    }

    [Fact]
    public void Parse_WithoutRecipesFile_EmptyRecipes()
    {
        Assert.Empty(ContentLoader.Parse(SurvivalContentFactory.ItemsJson, SurvivalContentFactory.YieldsJson, SurvivalContentFactory.StartJson).Recipes);
        Assert.Empty(ContentLoader.Parse(SurvivalContentFactory.ItemsJson, SurvivalContentFactory.YieldsJson, SurvivalContentFactory.StartJson, null, null).Recipes);

        // The directory loader treats a missing file as no recipes.
        var root = Path.Combine(Path.GetTempPath(), "horyzont-recipes-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "items"));
            Directory.CreateDirectory(Path.Combine(root, "terrain"));
            File.WriteAllText(Path.Combine(root, ContentLoader.ItemsFile), SurvivalContentFactory.ItemsJson);
            File.WriteAllText(Path.Combine(root, ContentLoader.YieldsFile), SurvivalContentFactory.YieldsJson);
            File.WriteAllText(Path.Combine(root, ContentLoader.StartFile), SurvivalContentFactory.StartJson);

            Assert.Empty(ContentLoader.LoadDirectory(root).Recipes);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Parse_UnknownIngredient_Throws()
    {
        Assert.Single(Parse(Smelt).Recipes);

        AssertInvalid("$.recipes[0].ingredients[0].item", Smelt.Replace("\"test:ore\"", "\"test:nothing\"", StringComparison.Ordinal));
        AssertInvalid("$.recipes[0].fuel[0].item", Smelt.Replace("\"test:peat\"", "\"test:nothing\"", StringComparison.Ordinal));
        AssertInvalid("$.recipes[0].result.item", Smelt.Replace("\"test:ingot\"", "\"test:nothing\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_RetiredItem_Throws()
    {
        // A retired item may not be made or used (spec 03, "Obsługa błędów").
        AssertInvalid("$.recipes[0].result.item", Smelt.Replace("\"test:ingot\"", "\"test:old\"", StringComparison.Ordinal));
        AssertInvalid("$.recipes[0].ingredients[0].item", Smelt.Replace("\"test:ore\"", "\"test:old\"", StringComparison.Ordinal));
        AssertInvalid("$.recipes[0].fuel[0].item", Smelt.Replace("\"test:peat\"", "\"test:old\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_FuelOnInstantStation_Throws()
    {
        const string HandWithFuel = """
            { "key": "t:hand", "station": "hands", "level": 1, "fuel": [ { "item": "test:peat", "count": 1 } ],
              "ingredients": [ { "item": "test:stone", "count": 2 } ], "result": { "item": "test:ingot", "count": 1 } }
            """;
        Assert.Single(Parse(Hand).Recipes);

        AssertInvalid("$.recipes[0].fuel", HandWithFuel);
    }

    [Theory]
    [InlineData("\"timeSeconds\": 20", "\"timeSeconds\": 0")]
    [InlineData("\"timeSeconds\": 20", "\"timeSeconds\": 3601")]
    [InlineData("\"timeSeconds\": 20", "\"timeSeconds\": \"fast\"")]
    [InlineData(", \"timeSeconds\": 20", "")]
    public void Parse_TimedWithoutValidTime_Throws(string from, string to)
    {
        Assert.Equal(20u, Parse(Smelt).Recipes[0].TimeSeconds);

        AssertInvalid("$.recipes[0].timeSeconds", Smelt.Replace(from, to, StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_TimeOnInstantStation_Throws()
    {
        AssertInvalid("$.recipes[0].timeSeconds", Hand.Replace("\"level\": 1,", "\"level\": 1, \"timeSeconds\": 5,", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("furnace", 2)]
    [InlineData("furnace", 0)]
    [InlineData("workbench", 5)]
    [InlineData("hands", 2)]
    [InlineData("kitchen", 2)]
    public void Parse_LevelOutsideTheStationRange_Throws(string station, int level)
    {
        // The workbench goes up to level 4; every other station has level 1.
        Assert.Equal(4, Parse(Hand.Replace("\"hands\"", "\"workbench\"", StringComparison.Ordinal).Replace("\"level\": 1", "\"level\": 4", StringComparison.Ordinal)).Recipes[0].Level);

        // The furnace rows use the recipe of a station with time (review B1, 8), so they do not depend on which of the checks comes first.
        var template = station == "furnace" ? Smelt : Hand.Replace("\"hands\"", $"\"{station}\"", StringComparison.Ordinal);
        AssertInvalid("$.recipes[0].level", template.Replace("\"level\": 1", $"\"level\": {level}", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_UnsupportedFormatVersion_Throws()
    {
        // Review B1, 8: the file has its own format version, 1.
        Assert.Single(Parse(Smelt).Recipes);

        var exception = Assert.Throws<InvalidDataException>(() => ContentLoader.Parse(Items, Yields, Start, null, "{ \"formatVersion\": 2, \"recipes\": [ " + Smelt + " ] }"));
        Assert.Contains(ContentLoader.RecipesFile, exception.Message, StringComparison.Ordinal);
        Assert.Contains("$.formatVersion", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DuplicateKey_Throws()
    {
        Assert.Equal(2, Parse(Smelt, Hand).Recipes.Count);

        AssertInvalid("$.recipes[1].key", Smelt, Smelt);
    }

    [Theory]
    [InlineData("\"key\": \"t:smelt\"", "\"key\": \"\"")]
    [InlineData("\"key\": \"t:smelt\"", "\"key\": \"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\"")]
    [InlineData("\"key\": \"t:smelt\"", "\"key\": 7")]
    [InlineData("\"station\": \"furnace\"", "\"station\": \"forge\"")]
    [InlineData("\"station\": \"furnace\"", "\"station\": \"none\"")]
    public void Parse_BadKeyOrStation_Throws(string from, string to)
    {
        Parse(Smelt);
        var path = from.Contains("station", StringComparison.Ordinal) ? "$.recipes[0].station" : "$.recipes[0].key";

        AssertInvalid(path, Smelt.Replace(from, to, StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_ResultCountAboveStack_Throws()
    {
        // The ingot stacks to 5.
        Assert.Equal(5, Parse(Smelt.Replace("\"result\": { \"item\": \"test:ingot\", \"count\": 1 }", "\"result\": { \"item\": \"test:ingot\", \"count\": 5 }", StringComparison.Ordinal)).Recipes[0].Result.Count);

        AssertInvalid(
            "$.recipes[0].result.count",
            Smelt.Replace("\"result\": { \"item\": \"test:ingot\", \"count\": 1 }", "\"result\": { \"item\": \"test:ingot\", \"count\": 6 }", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"item\": \"test:ore\", \"count\": 2", "\"item\": \"test:ore\", \"count\": 0", "$.recipes[0].ingredients[0].count")]
    [InlineData("\"item\": \"test:ore\", \"count\": 2", "\"item\": \"test:ore\", \"count\": 1000", "$.recipes[0].ingredients[0].count")]
    [InlineData("\"item\": \"test:peat\", \"count\": 1", "\"item\": \"test:peat\", \"count\": 0", "$.recipes[0].fuel[0].count")]
    [InlineData("\"item\": \"test:ingot\", \"count\": 1", "\"item\": \"test:ingot\", \"count\": 0", "$.recipes[0].result.count")]
    [InlineData("\"ingredients\": [ { \"item\": \"test:ore\", \"count\": 2 } ]", "\"ingredients\": [ ]", "$.recipes[0].ingredients")]
    [InlineData(
        "\"ingredients\": [ { \"item\": \"test:ore\", \"count\": 2 } ]",
        "\"ingredients\": [ { \"item\": \"test:ore\", \"count\": 2 }, { \"item\": \"test:ore\", \"count\": 1 } ]",
        "$.recipes[0].ingredients[1]")]
    [InlineData(
        "\"fuel\": [ { \"item\": \"test:peat\", \"count\": 1 } ]",
        "\"fuel\": [ { \"item\": \"test:ore\", \"count\": 1 } ]",
        "$.recipes[0].fuel[0]")]
    [InlineData(
        "\"fuel\": [ { \"item\": \"test:peat\", \"count\": 1 } ]",
        "\"fuel\": [ { \"item\": \"test:peat\", \"count\": 1 }, { \"item\": \"test:stone\", \"count\": 1 }, { \"item\": \"test:tool\", \"count\": 1 } ]",
        "$.recipes[0].fuel")]
    [InlineData("\"progressKey\": null", "\"progressKey\": \"\"", "$.recipes[0].progressKey")]
    [InlineData("\"progressKey\": null", "\"progressKey\": \"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\"", "$.recipes[0].progressKey")]
    public void Parse_BadEntries_Throw(string from, string to, string path)
    {
        Parse(Smelt);

        AssertInvalid(path, Smelt.Replace(from, to, StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_UnpairedSurrogate_Throws()
    {
        // A lone surrogate has no UTF-8 form (review A1 note d, review B1 note 8). It is built at run time: an attribute argument would turn it into U+FFFD.
        var lone = "a" + (char)0xD800 + "b";
        Parse(Smelt);

        AssertInvalid("$.recipes[0].key", Smelt.Replace("\"t:smelt\"", $"\"{lone}\"", StringComparison.Ordinal));
        AssertInvalid("$.recipes[0].progressKey", Smelt.Replace("\"progressKey\": null", $"\"progressKey\": \"{lone}\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_NineIngredients_Throws()
    {
        static string Ingredients(int count) => string.Join(", ", Enumerable.Repeat("{ \"item\": \"test:ore\", \"count\": 1 }", count));

        // The count is checked before the entries, so nine entries (repeats included) name the list, not an entry.
        var nine = Smelt.Replace("{ \"item\": \"test:ore\", \"count\": 2 }", Ingredients(9), StringComparison.Ordinal);
        var exception = Assert.Throws<InvalidDataException>(() => Parse(nine));
        Assert.Contains("$.recipes[0].ingredients", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ingredients[", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ProgressKey_ValidAndNull()
    {
        var withKey = Smelt.Replace("\"progressKey\": null", "\"progressKey\": \"test:boss_2\"", StringComparison.Ordinal);

        Assert.Equal("test:boss_2", Parse(withKey).Recipes[0].ProgressKey);
        Assert.Null(Parse(Smelt).Recipes[0].ProgressKey);
        Assert.Null(Parse(Hand).Recipes[0].ProgressKey);
    }

    [Fact]
    public void Parse_MoreThan1024Recipes_Throws()
    {
        static string Numbered(int i) => Hand.Replace("\"t:hand\"", $"\"t:r{i}\"", StringComparison.Ordinal);

        Assert.Equal(1024, Parse([.. Enumerable.Range(0, 1024).Select(Numbered)]).Recipes.Count);

        var exception = Assert.Throws<InvalidDataException>(() => Parse([.. Enumerable.Range(0, 1025).Select(Numbered)]));
        Assert.Contains("$.recipes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ItemOnlyFromRecipe_HasSource()
    {
        // The ingot has no yield and is not in the start kit: without a recipe that makes it, the item has no source (K6-29W D16).
        var withoutRecipes = Assert.Throws<InvalidDataException>(() => ContentLoader.Parse(Items, Yields, Start));
        Assert.Contains("$.items[4]", withoutRecipes.Message, StringComparison.Ordinal);

        Assert.Single(Parse(Smelt).Recipes);
    }

    [Fact]
    public void Parse_RepoContent_HasDemoRecipes()
    {
        var content = ContentLoader.LoadDirectory(Path.Combine(RepoRoot(), "game", "content"));

        // The tuning numbers (counts, seconds) are data; the test pins the shape of the table of K6-32 D24: stations, levels, item keys.
        (string Key, StationKind Station, int Level, string[] Ingredients, string[] Fuel)[] expected =
        [
            ("core:peat", StationKind.Hands, 1, ["core:soil"], []),
            ("core:stone_brick", StationKind.Hands, 1, ["core:stone"], []),
            ("core:iron_ingot", StationKind.Furnace, 1, ["core:ore"], ["core:peat"]),
            ("core:glass", StationKind.Furnace, 1, ["core:sand"], ["core:peat"]),
            ("core:fuel_cell", StationKind.Refinery, 1, ["core:iron_ingot", "core:sand"], ["core:peat"]),
            ("core:multitool_t2", StationKind.Workbench, 2, ["core:iron_ingot", "core:glass", "core:stone_brick"], []),
        ];
        foreach (var (key, station, level, ingredients, fuel) in expected)
        {
            Assert.True(content.TryGetRecipe(key, out var recipe), key);
            Assert.Equal((station, (byte)level), (recipe.Station, recipe.Level));
            Assert.Equal(ingredients, recipe.Ingredients.Select(i => i.ItemKey));
            Assert.Equal(fuel, recipe.Fuel.Select(i => i.ItemKey));
            Assert.Equal(key, recipe.Result.ItemKey);
            Assert.All([.. recipe.Ingredients, .. recipe.Fuel, recipe.Result], entry => Assert.True(entry.Count >= 1));
            Assert.Equal(station is StationKind.Furnace or StationKind.Refinery, recipe.TimeSeconds > 0);
            Assert.Null(recipe.ProgressKey);
        }
    }

    [Fact]
    public void Parse_RepoContent_DemoItemsAndEmptyStations()
    {
        var content = ContentLoader.LoadDirectory(Path.Combine(RepoRoot(), "game", "content"));

        // The fabricator has no recipes in the game yet (D24); the meals of the kitchen are K6-33 (D9, see FoodContentTests).
        Assert.DoesNotContain(content.Recipes, r => r.Station is StationKind.Fabricator);
        Assert.True(content.Items.TryGet("core:multitool_t2", out var tier2));
        Assert.Equal(2, tier2.ToolTier);
        foreach (var key in new[] { "core:peat", "core:stone_brick", "core:iron_ingot", "core:glass", "core:fuel_cell" })
        {
            Assert.True(content.Items.TryGet(key, out var item), key);
            Assert.Equal(ItemCategory.Component, item.Category);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Horyzont.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
