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

    /// <summary>Parses the test content.</summary>
    public static SurvivalContent CreateTest() => ContentLoader.Parse(ItemsJson, YieldsJson, StartJson);

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
    public static SurvivalContent CreateTestWithRecipes() => ContentLoader.Parse(CraftingItemsJson, CraftingYieldsJson, StartJson, RecipesJson);

    /// <summary>The content of the crafting world (the twelve items) without recipes: crafting and the automatic discoveries are off.</summary>
    public static SurvivalContent CreateCraftingWithoutRecipes() => ContentLoader.Parse(CraftingItemsJson, CraftingYieldsJson, StartJson);
}
