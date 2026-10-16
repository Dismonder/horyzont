using Horyzont.Core.Content;
using Horyzont.Core.Props;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Content;

/// <summary>
/// The props file <c>props/props.json</c> and its validation (K6-31 D8). Every refusal test first parses the valid twin, because content
/// without a props file loads today and a refusal alone would prove nothing.
/// </summary>
public sealed class PropContentTests
{
    private static readonly PropKindV1[] _kinds = [.. Enum.GetValues<PropKindV1>().Where(k => k != PropKindV1.None)];

    private static SurvivalContent Parse(string propsJson) => ContentLoader.Parse(
        SurvivalContentFactory.PropItemsJson, SurvivalContentFactory.PropYieldsJson, SurvivalContentFactory.PropStartJson, propsJson);

    private static void AssertRefused(string propsJson, string jsonPathPart)
    {
        Assert.NotEqual(SurvivalContentFactory.PropsJson, propsJson);
        var valid = Parse(SurvivalContentFactory.PropsJson);
        Assert.Equal(_kinds.Length, valid.Props.Count);

        var ex = Assert.Throws<InvalidDataException>(() => Parse(propsJson));
        Assert.Contains(ContentLoader.PropsFile, ex.Message, StringComparison.Ordinal);
        Assert.Contains(jsonPathPart, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RepoContent_AllV1KindsHaveDefs()
    {
        Assert.True(File.Exists(Path.Combine(RepoContent.Directory, "props", "props.json")), "game/content/props/props.json is missing.");
        var content = RepoContent.Load();

        Assert.Equal(_kinds.Length, content.Props.Count);
        string[] keys = ["core:pine", "core:pine_tall", "core:broadleaf", "core:broadleaf_autumn", "core:bush", "core:rock", "core:boulder", "core:crystals"];
        for (var i = 0; i < _kinds.Length; i++)
        {
            Assert.True(content.TryGetProp(_kinds[i], out var def), $"{_kinds[i]} has no definition.");
            Assert.Equal(keys[i], def.Key);
            Assert.InRange(def.HitPoints, 1, 1000);
            Assert.InRange(def.Yield.Count, 1, 8);
            Assert.All(def.Yield, y =>
            {
                Assert.True(content.Items.TryGet(y.ItemKey, out _), $"{def.Key} yields unknown item {y.ItemKey}.");
                Assert.InRange(y.Count, 1, 999);
            });
        }

        // The new items of the yield (D8): resources that stack to 500.
        foreach (var key in new[] { "core:wood", "core:fiber", "core:crystal" })
        {
            Assert.True(content.Items.TryGet(key, out var item), key);
            Assert.Equal(ItemCategory.Resource, item.Category);
            Assert.Equal(500, item.MaxStack);
            Assert.True(item.Flags.HasFlag(ItemFlags.Bulk));
        }
    }

    [Fact]
    public void Parse_TestContent_ReadsEveryField()
    {
        var content = SurvivalContentFactory.CreateTestWithProps();

        Assert.Equal(_kinds.Length, content.Props.Count);
        Assert.True(content.TryGetProp(PropKindV1.Pine, out var pine));
        Assert.Equal(new PropDef("core:pine", 3, 1, [new PropYield("test:wood", 3)], 10), pine, PropDefComparer.Instance);
        Assert.True(content.TryGetProp(PropKindV1.Boulder, out var boulder));
        Assert.Equal(30, boulder.HitPoints);
        Assert.Equal(0u, boulder.RegrowSeconds);
        Assert.Equal(new[] { new PropYield("test:stone", 5), new PropYield("test:gem", 1) }, boulder.Yield);
        Assert.True(content.TryGetProp(PropKindV1.PineTall, out var tall));
        Assert.Equal(2, tall.ToolTier);
        Assert.False(content.TryGetProp(PropKindV1.None, out _));
    }

    [Fact]
    public void Parse_MissingKind_Throws()
    {
        // Every kind of layout V1 needs a definition, or its props could be hit but never harvested.
        var withoutBush = string.Join('\n', SurvivalContentFactory.PropsJson.Split('\n').Where(l => !l.Contains("core:bush", StringComparison.Ordinal)));

        AssertRefused(withoutBush, "$.props");
    }

    [Fact]
    public void Parse_UnknownKey_Throws() => AssertRefused(
        SurvivalContentFactory.PropsJson.Replace("core:rock", "core:dragon", StringComparison.Ordinal), "$.props");

    [Fact]
    public void Parse_DuplicateKey_Throws() => AssertRefused(
        SurvivalContentFactory.PropsJson.Replace("core:boulder", "core:rock", StringComparison.Ordinal), "$.props");

    [Fact]
    public void Parse_UnknownItem_Throws() => AssertRefused(
        SurvivalContentFactory.PropsJson.Replace("test:fiber", "test:string", StringComparison.Ordinal), "$.props[4].yield[0].item");

    [Fact]
    public void Parse_HitPointsZero_Throws() => AssertRefused(
        SurvivalContentFactory.PropsJson.Replace("\"hitPoints\": 1,", "\"hitPoints\": 0,", StringComparison.Ordinal), ".hitPoints");

    [Fact]
    public void Parse_HitPointsOver1000_Throws() => AssertRefused(
        SurvivalContentFactory.PropsJson.Replace("\"hitPoints\": 30,", "\"hitPoints\": 1001,", StringComparison.Ordinal), "$.props[6].hitPoints");

    [Fact]
    public void Parse_RegrowSecondsOverMax_Throws()
    {
        const string pine = "{ \"key\": \"core:pine\", \"hitPoints\": 3, \"toolTier\": 1, \"regrowSeconds\": 10,";
        Assert.Contains(pine, SurvivalContentFactory.PropsJson, StringComparison.Ordinal);

        // 30 days (2 592 000 s) is the most; the valid twin parses, one second more is refused with the path of the field.
        var most = SurvivalContentFactory.PropsJson.Replace(pine, pine.Replace("\"regrowSeconds\": 10", "\"regrowSeconds\": 2592000", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(2_592_000u, Parse(most).Props.Single(p => p.Key == "core:pine").RegrowSeconds);
        AssertRefused(
            SurvivalContentFactory.PropsJson.Replace(pine, pine.Replace("\"regrowSeconds\": 10", "\"regrowSeconds\": 2592001", StringComparison.Ordinal), StringComparison.Ordinal),
            "$.props[0].regrowSeconds");
    }

    [Fact]
    public void Parse_CountZero_Throws() => AssertRefused(
        SurvivalContentFactory.PropsJson.Replace("\"count\": 2 }", "\"count\": 0 }", StringComparison.Ordinal), ".count");

    [Fact]
    public void Parse_YieldOver8_Throws()
    {
        var nine = string.Join(", ", Enumerable.Repeat("{ \"item\": \"test:wood\", \"count\": 1 }", 9));
        var eight = string.Join(", ", Enumerable.Repeat("{ \"item\": \"test:wood\", \"count\": 1 }", 8));
        var oneWood = "\"yield\": [ { \"item\": \"test:wood\", \"count\": 3 } ] },\n    { \"key\": \"core:pine_tall\"";

        // Eight entries are the most (D8): the valid twin of the refusal below.
        Assert.Contains(oneWood, SurvivalContentFactory.PropsJson, StringComparison.Ordinal);
        var withEight = SurvivalContentFactory.PropsJson.Replace(oneWood, $"\"yield\": [ {eight} ] }},\n    {{ \"key\": \"core:pine_tall\"", StringComparison.Ordinal);
        Assert.Equal(_kinds.Length, Parse(withEight).Props.Count);

        AssertRefused(
            SurvivalContentFactory.PropsJson.Replace(oneWood, $"\"yield\": [ {nine} ] }},\n    {{ \"key\": \"core:pine_tall\"", StringComparison.Ordinal),
            "$.props[0].yield");
    }

    [Fact]
    public void Parse_EmptyYield_Throws() => AssertRefused(
        SurvivalContentFactory.PropsJson.Replace("\"yield\": [ { \"item\": \"test:fiber\", \"count\": 2 } ]", "\"yield\": [ ]", StringComparison.Ordinal),
        "$.props[4].yield");

    [Fact]
    public void Parse_ThreeFileOverload_NoProps()
    {
        // The four-file overload has props; the old three-file one stays as it was and leaves the rules of props off (D21).
        Assert.Equal(_kinds.Length, SurvivalContentFactory.CreateTestWithProps().Props.Count);

        var old = SurvivalContentFactory.CreateTest();

        Assert.Empty(old.Props);
        Assert.False(old.TryGetProp(PropKindV1.Pine, out _));
    }

    [Fact]
    public void HasSource_WoodFromPropsOnly_Accepted()
    {
        const string items = """
            { "items": [
              { "key": "test:tool", "category": "tool", "flags": ["tool"], "toolTier": 1 },
              { "key": "test:wood", "category": "resource", "flags": ["bulk"] } ] }
            """;
        const string yields = """{ "yields": [] }""";
        const string start = """
            { "containers": { "playerBag": 40, "hotbar": 10, "equipment": 7 },
              "items": [ { "item": "test:tool", "count": 1, "container": "hotbar", "slot": 0 } ] }
            """;
        var props = SurvivalContentFactory.PropsJson
            .Replace("test:fiber", "test:wood", StringComparison.Ordinal)
            .Replace("test:stone", "test:wood", StringComparison.Ordinal)
            .Replace("test:crystal", "test:wood", StringComparison.Ordinal)
            .Replace("""{ "item": "test:gem", "count": 1 }""", """{ "item": "test:wood", "count": 1 }""", StringComparison.Ordinal);

        // Wood is dropped by props alone: no terrain yield and no start kit item.
        var content = ContentLoader.Parse(items, yields, start, props);

        Assert.Equal(_kinds.Length, content.Props.Count);
        Assert.True(content.Items.TryGet("test:wood", out _));

        // Without the props file the same item has no source, as before.
        var ex = Assert.Throws<InvalidDataException>(() => ContentLoader.Parse(items, yields, start));
        Assert.Contains("no source", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Records compare their lists by reference; compares the fields of two definitions.</summary>
    private sealed class PropDefComparer : IEqualityComparer<PropDef>
    {
        public static PropDefComparer Instance { get; } = new();

        public bool Equals(PropDef? x, PropDef? y) =>
            x is not null && y is not null && x.Key == y.Key && x.HitPoints == y.HitPoints && x.ToolTier == y.ToolTier
            && x.RegrowSeconds == y.RegrowSeconds && x.Yield.SequenceEqual(y.Yield);

        public int GetHashCode(PropDef obj) => obj.Key.GetHashCode(StringComparison.Ordinal);
    }
}
