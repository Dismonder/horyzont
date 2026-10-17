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
}
