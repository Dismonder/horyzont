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
    /// The start kit of <see cref="StartJson"/> plus the tier 2 tool in hotbar slot 2. Wood and fibre are in the bag only so that the content
    /// parses while the props are not yet a source of items (K6-31 B3); the tests never apply the kit.
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
