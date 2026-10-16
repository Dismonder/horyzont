using Horyzont.Core.Content;

namespace Horyzont.Testing;

/// <summary>
/// Survival content for tests: a tool (stack 1), a gem (stack 5), stone and soil (stack 500). World material <c>stone</c> (id 1 in
/// <see cref="WorldFactory"/>) yields 2 stone per m³, <c>dirt</c> (id 2) 3 soil per m³; the start kit puts the tool in hotbar slot 0 and
/// three gems in hotbar slot 1.
/// </summary>
public static class SurvivalContentFactory
{
    public const string ToolKey = "test:tool";
    public const string GemKey = "test:gem";
    public const string StoneKey = "test:stone";
    public const string SoilKey = "test:soil";
    public const string BerriesKey = "test:berries";
    public const string JamKey = "test:jam";
    public const string BrothKey = "test:broth";
    public const string BreadKey = "test:bread";

    public const string ItemsJson = """
        {
          "items": [
            { "key": "test:tool", "category": "tool", "flags": ["tool"], "toolTier": 1, "visual": { "glyph": "T" } },
            { "key": "test:gem", "category": "component", "maxStack": 5, "maxStackOverride": true },
            { "key": "test:stone", "category": "resource", "flags": ["bulk"] },
            { "key": "test:soil", "category": "resource", "flags": ["bulk"] }
          ]
        }
        """;

    public const string YieldsJson = """
        {
          "yields": [
            { "material": "stone", "item": "test:stone", "itemsPerCubicMeter": 2, "toolTier": 1 },
            { "material": "dirt", "item": "test:soil", "itemsPerCubicMeter": 3, "toolTier": 1 }
          ]
        }
        """;

    public const string StartJson = """
        {
          "containers": { "playerBag": 40, "hotbar": 10, "equipment": 7 },
          "items": [
            { "item": "test:tool", "count": 1, "container": "hotbar", "slot": 0 },
            { "item": "test:gem", "count": 3, "container": "hotbar", "slot": 1 }
          ]
        }
        """;

    /// <summary>Item keys in the order of <see cref="ItemsJson"/>; a world created with them gets ids 1..4 in this order.</summary>
    public static IReadOnlyList<string> ItemKeys { get; } = [ToolKey, GemKey, StoneKey, SoilKey];

    /// <summary>
    /// <see cref="ItemKeys"/> and four food types (K6-33): a world created with them gets ids 1..8, berries 5, jam 6, broth 7, bread 8. The statistics of the
    /// food are in <c>FoodContent</c> of the Core tests (the content loader reads the <c>food</c> field in step B3 of K6-33).
    /// </summary>
    public static IReadOnlyList<string> FoodItemKeys { get; } = [.. ItemKeys, BerriesKey, JamKey, BrothKey, BreadKey];

    public const string HeatCellKey = "test:heat_cell";
    public const string OxygenCanisterKey = "test:oxygen_canister";
    public const string TankModuleKey = "test:module_o2_tank";
    public const string ThermalModuleKey = "test:module_thermal";
    public const string FuelPackKey = "test:fuel_pack";

    /// <summary>
    /// <see cref="FoodItemKeys"/> and five types of K6-34 (ids 9..13): a heat cell and an oxygen canister (suit refills), an oxygen tank and a thermal module (suit modules) and the
    /// fuel of the oxygen generator. Their statistics are in <c>HazardTestContent</c> of the Core tests (the loader reads them in step B3).
    /// </summary>
    public static IReadOnlyList<string> HazardItemKeys { get; } = [.. FoodItemKeys, HeatCellKey, OxygenCanisterKey, TankModuleKey, ThermalModuleKey, FuelPackKey];

    /// <summary>Parses the test content.</summary>
    public static SurvivalContent CreateTest() => ContentLoader.Parse(ItemsJson, YieldsJson, StartJson);

    /// <summary>
    /// The content of <see cref="CreateTest"/> plus the four foods of <see cref="FoodItemKeys"/> (K6-33): berries 10 health, 20 stamina, 0.5/s, 600 s; jam 25, 35,
    /// 1.0/s, 1200 s; broth 15, 0, 1.5/s, 900 s; bread 20, 10, 0.25/s, 1800 s.
    /// </summary>
    /// <returns>The content.</returns>
    public static SurvivalContent CreateTestWithFood() => ContentLoader.Parse(FoodItemsJson, YieldsJson, FoodStartJson);

    // The loader wants a source for every item; the foods come from the start kit here (the Core and server tests create worlds without applying it).
    private const string FoodStartJson = """
        {
          "containers": { "playerBag": 40, "hotbar": 10, "equipment": 7 },
          "items": [
            { "item": "test:tool", "count": 1, "container": "hotbar", "slot": 0 },
            { "item": "test:gem", "count": 3, "container": "hotbar", "slot": 1 },
            { "item": "test:berries", "count": 1, "container": "playerBag", "slot": 30 },
            { "item": "test:jam", "count": 1, "container": "playerBag", "slot": 31 },
            { "item": "test:broth", "count": 1, "container": "playerBag", "slot": 32 },
            { "item": "test:bread", "count": 1, "container": "playerBag", "slot": 33 }
          ]
        }
        """;

    private const string FoodItemsJson = """
        {
          "items": [
            { "key": "test:tool", "category": "tool", "flags": ["tool"], "toolTier": 1, "visual": { "glyph": "T" } },
            { "key": "test:gem", "category": "component", "maxStack": 5, "maxStackOverride": true },
            { "key": "test:stone", "category": "resource", "flags": ["bulk"] },
            { "key": "test:soil", "category": "resource", "flags": ["bulk"] },
            { "key": "test:berries", "category": "food", "flags": ["food"], "maxStack": 20, "maxStackOverride": true,
              "food": { "health": 10, "stamina": 20, "regen": 0.5, "durationSeconds": 600 } },
            { "key": "test:jam", "category": "food", "flags": ["food"], "maxStack": 20, "maxStackOverride": true,
              "food": { "health": 25, "stamina": 35, "regen": 1.0, "durationSeconds": 1200 } },
            { "key": "test:broth", "category": "food", "flags": ["food"], "maxStack": 20, "maxStackOverride": true,
              "food": { "health": 15, "stamina": 0, "regen": 1.5, "durationSeconds": 900 } },
            { "key": "test:bread", "category": "food", "flags": ["food"], "maxStack": 20, "maxStackOverride": true,
              "food": { "health": 20, "stamina": 10, "regen": 0.25, "durationSeconds": 1800 } }
          ]
        }
        """;

    public const string OreKey = "test:ore";
    public const string PeatKey = "test:peat";
    public const string BrickKey = "test:brick";
    public const string IngotKey = "test:ingot";
    public const string PlateKey = "test:plate";
    public const string FuelCellKey = "test:fuelcell";
    public const string StewKey = "test:stew";
    public const string ModuleKey = "test:module";

    /// <summary>Progress key the fabricator recipe <c>t:fab</c> needs.</summary>
    public const string Boss2Key = "test:boss_2";

    /// <summary>
    /// The four items of <see cref="ItemsJson"/> plus ore, peat, brick, an ingot (stack 5), a plate, a fuel cell, a stew and a module (stack 500 each, K6-32).
    /// Every new item has a terrain yield, so the content is valid with and without recipes.
    /// </summary>
    public const string CraftingItemsJson = """
        {
          "items": [
            { "key": "test:tool", "category": "tool", "flags": ["tool"], "toolTier": 1, "visual": { "glyph": "T" } },
            { "key": "test:gem", "category": "component", "maxStack": 5, "maxStackOverride": true },
            { "key": "test:stone", "category": "resource", "flags": ["bulk"] },
            { "key": "test:soil", "category": "resource", "flags": ["bulk"] },
            { "key": "test:ore", "category": "resource", "flags": ["bulk"] },
            { "key": "test:peat", "category": "resource", "flags": ["bulk"] },
            { "key": "test:brick", "category": "resource", "flags": ["bulk"] },
            { "key": "test:ingot", "category": "component", "maxStack": 5, "maxStackOverride": true },
            { "key": "test:plate", "category": "resource", "flags": ["bulk"] },
            { "key": "test:fuelcell", "category": "resource", "flags": ["bulk"] },
            { "key": "test:stew", "category": "resource", "flags": ["bulk"] },
            { "key": "test:module", "category": "resource", "flags": ["bulk"] }
          ]
        }
        """;

    public const string CraftingYieldsJson = """
        {
          "yields": [
            { "material": "stone", "item": "test:stone", "itemsPerCubicMeter": 2, "toolTier": 1 },
            { "material": "dirt", "item": "test:soil", "itemsPerCubicMeter": 3, "toolTier": 1 },
            { "material": "ore", "item": "test:ore", "itemsPerCubicMeter": 1, "toolTier": 1 },
            { "material": "peat", "item": "test:peat", "itemsPerCubicMeter": 1, "toolTier": 1 },
            { "material": "brick", "item": "test:brick", "itemsPerCubicMeter": 1, "toolTier": 1 },
            { "material": "ingot", "item": "test:ingot", "itemsPerCubicMeter": 1, "toolTier": 1 },
            { "material": "plate", "item": "test:plate", "itemsPerCubicMeter": 1, "toolTier": 1 },
            { "material": "fuelcell", "item": "test:fuelcell", "itemsPerCubicMeter": 1, "toolTier": 1 },
            { "material": "stew", "item": "test:stew", "itemsPerCubicMeter": 1, "toolTier": 1 },
            { "material": "module", "item": "test:module", "itemsPerCubicMeter": 1, "toolTier": 1 }
          ]
        }
        """;

    /// <summary>
    /// A recipe for every station (K6-32 D4): <c>t:hand</c> (hands, 2 stone to a brick), <c>t:bench1</c> to <c>t:bench3</c> (workbench levels 1 to 3),
    /// <c>t:cook</c> (kitchen), <c>t:fab</c> (fabricator, needs the progress key <see cref="Boss2Key"/>), <c>t:smelt</c> (furnace, 1 s = 60 ticks, fuel peat) and
    /// <c>t:refine</c> (refinery, 2 s, fuel peat).
    /// </summary>
    public const string RecipesJson = """
        {
          "formatVersion": 1,
          "recipes": [
            { "key": "t:hand", "station": "hands", "level": 1,
              "ingredients": [ { "item": "test:stone", "count": 2 } ], "result": { "item": "test:brick", "count": 1 } },
            { "key": "t:bench1", "station": "workbench", "level": 1,
              "ingredients": [ { "item": "test:brick", "count": 2 } ], "result": { "item": "test:gem", "count": 1 } },
            { "key": "t:bench2", "station": "workbench", "level": 2,
              "ingredients": [ { "item": "test:brick", "count": 1 }, { "item": "test:ingot", "count": 1 } ], "result": { "item": "test:plate", "count": 1 } },
            { "key": "t:bench3", "station": "workbench", "level": 3,
              "ingredients": [ { "item": "test:ingot", "count": 2 } ], "result": { "item": "test:fuelcell", "count": 1 } },
            { "key": "t:cook", "station": "kitchen", "level": 1,
              "ingredients": [ { "item": "test:ore", "count": 1 }, { "item": "test:soil", "count": 1 } ], "result": { "item": "test:stew", "count": 1 } },
            { "key": "t:fab", "station": "fabricator", "level": 1, "progressKey": "test:boss_2",
              "ingredients": [ { "item": "test:plate", "count": 1 }, { "item": "test:ingot", "count": 1 } ], "result": { "item": "test:module", "count": 1 } },
            { "key": "t:smelt", "station": "furnace", "level": 1, "timeSeconds": 1,
              "ingredients": [ { "item": "test:ore", "count": 2 } ], "fuel": [ { "item": "test:peat", "count": 1 } ],
              "result": { "item": "test:ingot", "count": 1 } },
            { "key": "t:smelt2", "station": "furnace", "level": 1, "timeSeconds": 2,
              "ingredients": [ { "item": "test:soil", "count": 2 } ], "result": { "item": "test:brick", "count": 1 } },
            { "key": "t:refine", "station": "refinery", "level": 1, "timeSeconds": 2,
              "ingredients": [ { "item": "test:ingot", "count": 1 }, { "item": "test:soil", "count": 2 } ], "fuel": [ { "item": "test:peat", "count": 1 } ],
              "result": { "item": "test:fuelcell", "count": 1 } }
          ]
        }
        """;

    /// <summary>Item keys of <see cref="CraftingItemsJson"/> in order; the first four are <see cref="ItemKeys"/>, so a world created with them gets ids 1..12.</summary>
    public static IReadOnlyList<string> CraftingItemKeys { get; } =
        [ToolKey, GemKey, StoneKey, SoilKey, OreKey, PeatKey, BrickKey, IngotKey, PlateKey, FuelCellKey, StewKey, ModuleKey];

    /// <summary>The content of the crafting world with the recipes of <see cref="RecipesJson"/> (K6-32); <see cref="CreateTest"/> stays without recipes.</summary>
    public static SurvivalContent CreateTestWithRecipes() => ContentLoader.Parse(CraftingItemsJson, CraftingYieldsJson, StartJson, null, RecipesJson);

    /// <summary>The content of the crafting world (the twelve items) without recipes: crafting and the automatic discoveries are off.</summary>
    public static SurvivalContent CreateCraftingWithoutRecipes() => ContentLoader.Parse(CraftingItemsJson, CraftingYieldsJson, StartJson);


    public const string WoodKey = "test:wood";
    public const string FiberKey = "test:fiber";
    public const string CrystalKey = "test:crystal";
    public const string Tool2Key = "test:tool2";

    /// <summary>World item keys of <see cref="CreateTestWithProps"/>: <see cref="ItemKeys"/> (ids 1..4), then wood, fibre, crystal and the tier 2 tool.</summary>
    public static IReadOnlyList<string> PropItemKeys { get; } = [.. ItemKeys, WoodKey, FiberKey, CrystalKey, Tool2Key];

    /// <summary>World material keys of the world made for <see cref="CreateTestWithProps"/>: stone 1, dirt 2, bedrock 3 (tier 2 yield), ash 4 (no yield).</summary>
    public static IReadOnlyList<string> PropMaterialKeys { get; } = ["stone", "dirt", "bedrock", "ash"];

    /// <summary>The items of <see cref="ItemsJson"/> plus wood, fibre, crystal (resources) and a tier 2 tool.</summary>
    public const string PropItemsJson = """
        {
          "items": [
            { "key": "test:tool", "category": "tool", "flags": ["tool"], "toolTier": 1, "visual": { "glyph": "T" } },
            { "key": "test:gem", "category": "component", "maxStack": 5, "maxStackOverride": true },
            { "key": "test:stone", "category": "resource", "flags": ["bulk"] },
            { "key": "test:soil", "category": "resource", "flags": ["bulk"] },
            { "key": "test:wood", "category": "resource", "flags": ["bulk"] },
            { "key": "test:fiber", "category": "resource", "flags": ["bulk"] },
            { "key": "test:crystal", "category": "resource", "flags": ["bulk"] },
            { "key": "test:tool2", "category": "tool", "flags": ["tool"], "toolTier": 2 }
          ]
        }
        """;

    /// <summary>Stone and dirt as in <see cref="YieldsJson"/> (tier 1) and bedrock, which needs a tier 2 tool; "ash" has no entry.</summary>
    public const string PropYieldsJson = """
        {
          "yields": [
            { "material": "stone", "item": "test:stone", "itemsPerCubicMeter": 2, "toolTier": 1 },
            { "material": "dirt", "item": "test:soil", "itemsPerCubicMeter": 3, "toolTier": 1 },
            { "material": "bedrock", "item": "test:crystal", "itemsPerCubicMeter": 1, "toolTier": 2 }
          ]
        }
        """;

    /// <summary>
    /// The start kit of <see cref="StartJson"/> plus the tier 2 tool in hotbar slot 2. Wood and fibre are in the bag as well, which keeps
    /// the content parsing without <see cref="PropsJson"/> (the three-file overload); the tests never apply the kit.
    /// </summary>
    public const string PropStartJson = """
        {
          "containers": { "playerBag": 40, "hotbar": 10, "equipment": 7 },
          "items": [
            { "item": "test:tool", "count": 1, "container": "hotbar", "slot": 0 },
            { "item": "test:gem", "count": 3, "container": "hotbar", "slot": 1 },
            { "item": "test:tool2", "count": 1, "container": "hotbar", "slot": 2 },
            { "item": "test:wood", "count": 1, "container": "playerBag", "slot": 0 },
            { "item": "test:fiber", "count": 1, "container": "playerBag", "slot": 1 }
          ]
        }
        """;

    /// <summary>
    /// The props of layout V1 with small hit points: pine 3 (tier 1), tall pine 4 (tier 2), broadleaf and autumn broadleaf 3 (tier 1), bush 1
    /// and rock 1 (hand), boulder 30 (tier 1, two items), crystals 2 (tier 1). Trees and the bush regrow after 10 s, the rest never.
    /// </summary>
    public const string PropsJson = """
        {
          "formatVersion": 1,
          "props": [
            { "key": "core:pine", "hitPoints": 3, "toolTier": 1, "regrowSeconds": 10, "yield": [ { "item": "test:wood", "count": 3 } ] },
            { "key": "core:pine_tall", "hitPoints": 4, "toolTier": 2, "regrowSeconds": 10, "yield": [ { "item": "test:wood", "count": 5 } ] },
            { "key": "core:broadleaf", "hitPoints": 3, "toolTier": 1, "regrowSeconds": 10, "yield": [ { "item": "test:wood", "count": 3 } ] },
            { "key": "core:broadleaf_autumn", "hitPoints": 3, "toolTier": 1, "regrowSeconds": 10, "yield": [ { "item": "test:wood", "count": 3 } ] },
            { "key": "core:bush", "hitPoints": 1, "toolTier": 0, "regrowSeconds": 10, "yield": [ { "item": "test:fiber", "count": 2 } ] },
            { "key": "core:rock", "hitPoints": 1, "toolTier": 0, "regrowSeconds": 0, "yield": [ { "item": "test:stone", "count": 2 } ] },
            { "key": "core:boulder", "hitPoints": 30, "toolTier": 1, "regrowSeconds": 0, "yield": [ { "item": "test:stone", "count": 5 }, { "item": "test:gem", "count": 1 } ] },
            { "key": "core:crystals", "hitPoints": 2, "toolTier": 1, "regrowSeconds": 0, "yield": [ { "item": "test:crystal", "count": 1 } ] }
          ]
        }
        """;

    /// <summary>Parses the test content with the props (K6-31).</summary>
    public static SurvivalContent CreateTestWithProps() => ContentLoader.Parse(PropItemsJson, PropYieldsJson, PropStartJson, PropsJson);
}
