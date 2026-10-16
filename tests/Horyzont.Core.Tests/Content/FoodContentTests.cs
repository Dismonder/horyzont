using Horyzont.Core.Content;
using Horyzont.Core.Props;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Core.Tests.Content;

/// <summary>
/// The <c>food</c> field of <c>items.json</c> (K6-33 D8): an object with <c>health</c> and <c>stamina</c> (integers 0 to 500, at least one above 0),
/// <c>regen</c> (finite, 0 to 50) and <c>durationSeconds</c> (integer 30 to 7200), all four required; present exactly when the flag <c>food</c> is, and
/// never on a retired item. Every error is an <see cref="InvalidDataException"/> naming the file and the JSON path. Every refusal test first parses the
/// valid twin. Until step B3 the loader ignores the field, so these tests fail on it.
/// </summary>
public sealed class FoodContentTests
{
    private const string Yields = """{ "yields": [ { "material": "stone", "item": "test:stone", "itemsPerCubicMeter": 2, "toolTier": 1 } ] }""";

    private const string Start = """
        { "containers": { "playerBag": 40, "hotbar": 10, "equipment": 7 },
          "items": [ { "item": "test:tool", "count": 1, "container": "hotbar", "slot": 0 },
                     { "item": "test:berries", "count": 1, "container": "hotbar", "slot": 1 } ] }
        """;

    private const string Tool = """{ "key": "test:tool", "category": "tool", "flags": ["tool"], "toolTier": 1 }""";

    private const string Stone = """{ "key": "test:stone", "category": "resource", "flags": ["bulk"] }""";

    private const string ValidFood = """{ "health": 20, "stamina": 30, "regen": 1.0, "durationSeconds": 1200 }""";

    private static string Berries(string flags, string? food) =>
        "{ \"key\": \"test:berries\", \"category\": \"food\", \"flags\": [" + flags + "]" + (food is null ? string.Empty : ", \"food\": " + food) + " }";

    private static string Items(string berries) => "{ \"items\": [ " + Tool + ", " + Stone + ", " + berries + " ] }";

    private static SurvivalContent Parse(string food, string flags = "\"food\"") => ContentLoader.Parse(Items(Berries(flags, food)), Yields, Start);

    private static void AssertInvalid(string path, Action parse)
    {
        var e = Assert.Throws<InvalidDataException>(parse);
        Assert.Contains(ContentLoader.ItemsFile, e.Message, StringComparison.Ordinal);
        Assert.Contains(path, e.Message, StringComparison.Ordinal);
    }

    private static string WithFood(string health = "20", string stamina = "30", string regen = "1.0", string duration = "1200") =>
        "{ \"health\": " + health + ", \"stamina\": " + stamina + ", \"regen\": " + regen + ", \"durationSeconds\": " + duration + " }";

    [Fact]
    public void Parse_FoodItem_ReadsFoodDef()
    {
        var content = Parse(ValidFood);

        Assert.True(content.Items.TryGet("test:berries", out var berries));
        Assert.Equal(new FoodDef(20, 30, 1.0f, 1200), berries.Food);
        Assert.Equal(ItemCategory.Food, berries.Category);
        Assert.True(berries.Flags.HasFlag(ItemFlags.Food));
        Assert.True(content.Items.TryGet("test:stone", out var stone));
        Assert.Null(stone.Food);
    }

    [Theory]
    [InlineData("0", "30", "1.0", "30")]
    [InlineData("500", "0", "0", "7200")]
    [InlineData("0", "500", "50", "1200")]
    [InlineData("1", "1", "0.25", "600")]
    public void Parse_FoodAtTheLimits_Accepted(string health, string stamina, string regen, string duration)
    {
        var content = Parse(WithFood(health, stamina, regen, duration));

        Assert.True(content.Items.TryGet("test:berries", out var berries));
        Assert.NotNull(berries.Food);
        Assert.Equal(ushort.Parse(health, System.Globalization.CultureInfo.InvariantCulture), berries.Food.Health);
        Assert.Equal(uint.Parse(duration, System.Globalization.CultureInfo.InvariantCulture), berries.Food.DurationSeconds);
    }

    [Fact]
    public void Parse_FoodFlagWithoutFood_Throws()
    {
        Assert.NotNull(Parse(ValidFood).Items.Items.Single(i => i.Key == "test:berries").Food);

        AssertInvalid("$.items[2].food", () => ContentLoader.Parse(Items(Berries("\"food\"", null)), Yields, Start));
    }

    [Fact]
    public void Parse_FoodWithoutFlag_Throws()
    {
        Assert.NotNull(Parse(ValidFood).Items.Items.Single(i => i.Key == "test:berries").Food);

        AssertInvalid("$.items[2].food", () => Parse(ValidFood, flags: string.Empty));
        AssertInvalid("$.items[2].food", () => Parse(ValidFood, flags: "\"bulk\""));
    }

    [Fact]
    public void Parse_FoodOnARetiredItem_Throws()
    {
        Assert.NotNull(Parse(ValidFood).Items.Items.Single(i => i.Key == "test:berries").Food);

        AssertInvalid("$.items[2].food", () => Parse(ValidFood, flags: "\"food\", \"retired\""));
    }

    [Fact]
    public void Parse_FoodAllZero_Throws()
    {
        Assert.NotNull(Parse(WithFood(health: "0", stamina: "1")).Items.Items.Single(i => i.Key == "test:berries").Food);
        Assert.NotNull(Parse(WithFood(health: "1", stamina: "0")).Items.Items.Single(i => i.Key == "test:berries").Food);

        // Health and stamina both 0: nothing to gain, whatever the regeneration is.
        AssertInvalid("$.items[2].food", () => Parse(WithFood(health: "0", stamina: "0", regen: "5.0")));
    }

    [Theory]
    [InlineData("health", "501")]
    [InlineData("health", "-1")]
    [InlineData("health", "1.5")]
    [InlineData("health", "\"many\"")]
    [InlineData("stamina", "501")]
    [InlineData("stamina", "-1")]
    [InlineData("stamina", "2.5")]
    public void Parse_FoodPointsOutOfRange_Throws(string field, string value)
    {
        Assert.NotNull(Parse(ValidFood).Items.Items.Single(i => i.Key == "test:berries").Food);

        var food = field == "health" ? WithFood(health: value) : WithFood(stamina: value);
        AssertInvalid("$.items[2].food." + field, () => Parse(food));
    }

    [Theory]
    [InlineData("29")]
    [InlineData("7201")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1200.5")]
    [InlineData("\"long\"")]
    public void Parse_FoodDurationOutOfRange_Throws(string duration)
    {
        Assert.NotNull(Parse(WithFood(duration: "30")).Items.Items.Single(i => i.Key == "test:berries").Food);
        Assert.NotNull(Parse(WithFood(duration: "7200")).Items.Items.Single(i => i.Key == "test:berries").Food);

        AssertInvalid("$.items[2].food.durationSeconds", () => Parse(WithFood(duration: duration)));
    }

    [Theory]
    [InlineData("50.01")]
    [InlineData("-0.5")]
    [InlineData("1e999")]
    [InlineData("\"fast\"")]
    public void Parse_FoodRegenNotFiniteOrAbove50_Throws(string regen)
    {
        Assert.NotNull(Parse(WithFood(regen: "50")).Items.Items.Single(i => i.Key == "test:berries").Food);

        AssertInvalid("$.items[2].food.regen", () => Parse(WithFood(regen: regen)));
    }

    [Theory]
    [InlineData("health")]
    [InlineData("stamina")]
    [InlineData("regen")]
    [InlineData("durationSeconds")]
    public void Parse_FoodFieldMissing_Throws(string field)
    {
        Assert.NotNull(Parse(ValidFood).Items.Items.Single(i => i.Key == "test:berries").Food);

        var fields = new Dictionary<string, string> { ["health"] = "20", ["stamina"] = "30", ["regen"] = "1.0", ["durationSeconds"] = "1200" };
        fields.Remove(field);
        var food = "{ " + string.Join(", ", fields.Select(f => $"\"{f.Key}\": {f.Value}")) + " }";

        AssertInvalid("$.items[2].food." + field, () => Parse(food));
    }

    [Fact]
    public void Parse_FoodNotAnObject_Throws()
    {
        Assert.NotNull(Parse(ValidFood).Items.Items.Single(i => i.Key == "test:berries").Food);

        AssertInvalid("$.items[2].food", () => Parse("5"));
        AssertInvalid("$.items[2].food", () => Parse("[]"));
    }

    [Fact]
    public void RepoContent_FoodItemsHaveSources()
    {
        var content = RepoContent.Load();

        // The numbers are data; the test pins the shape of D9: three foods of the category food with the flag and statistics in range, and each one
        // obtainable: the berries from the bush, the meals from the kitchen.
        foreach (var key in new[] { "core:berries", "core:berry_jam", "core:herbal_broth" })
        {
            Assert.True(content.Items.TryGet(key, out var item), key);
            Assert.Equal(ItemCategory.Food, item.Category);
            Assert.True(item.Flags.HasFlag(ItemFlags.Food));
            Assert.Equal(20, item.MaxStack);
            var food = Assert.IsType<FoodDef>(item.Food);
            Assert.InRange((int)food.Health, 0, 500);
            Assert.InRange((int)food.Stamina, 0, 500);
            Assert.True(food.Health > 0 || food.Stamina > 0);
            Assert.InRange(food.HealthRegenPerSecond, 0f, 50f);
            Assert.InRange((int)food.DurationSeconds, 30, 7200);
        }

        Assert.True(content.TryGetProp(PropKindV1.Bush, out var bush));
        Assert.Contains(bush.Yield, y => y.ItemKey == "core:berries" && y.Count >= 1);
        Assert.Contains(bush.Yield, y => y.ItemKey == "core:fiber");

        Assert.True(content.TryGetRecipe("core:berry_jam", out var jam));
        Assert.Equal((StationKind.Kitchen, (byte)1, 0u), (jam.Station, jam.Level, jam.TimeSeconds));
        Assert.Equal(["core:berries"], jam.Ingredients.Select(i => i.ItemKey));
        Assert.Equal("core:berry_jam", jam.Result.ItemKey);

        Assert.True(content.TryGetRecipe("core:herbal_broth", out var broth));
        Assert.Equal((StationKind.Kitchen, (byte)1, 0u), (broth.Station, broth.Level, broth.TimeSeconds));
        Assert.Equal(["core:berries", "core:fiber"], broth.Ingredients.Select(i => i.ItemKey).Order(StringComparer.Ordinal));
        Assert.Equal("core:herbal_broth", broth.Result.ItemKey);

        // Nothing else cooks yet: the fabricator stays empty (K6-32 D24) and the kitchen has exactly the two meals.
        Assert.Equal(["core:berry_jam", "core:herbal_broth"], content.Recipes.Where(r => r.Station == StationKind.Kitchen).Select(r => r.Key).Order(StringComparer.Ordinal));
    }
}
