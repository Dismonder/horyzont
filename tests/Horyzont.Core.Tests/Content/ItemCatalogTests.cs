using Horyzont.Core.Content;
using Horyzont.Core.Items;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Content;

public sealed class ItemCatalogTests
{
    private const string Yields = """{ "yields": [ { "material": "stone", "item": "test:stone", "itemsPerCubicMeter": 2, "toolTier": 1 } ] }""";

    private const string Start = """
        { "containers": { "playerBag": 40, "hotbar": 10, "equipment": 7 },
          "items": [ { "item": "test:tool", "count": 1, "container": "hotbar", "slot": 0 } ] }
        """;

    private const string Items = """
        { "items": [
          { "key": "test:tool", "category": "tool", "flags": ["tool"], "toolTier": 1 },
          { "key": "test:stone", "category": "resource", "flags": ["bulk"] } ] }
        """;

    [Fact]
    public void Parse_RepoContent_IsValid()
    {
        var content = ContentLoader.LoadDirectory(Path.Combine(RepoRoot(), "game", "content"));

        Assert.True(content.Items.TryGet("core:multitool", out var multitool));
        Assert.Equal(1, multitool.MaxStack);
        Assert.True(multitool.Flags.HasFlag(ItemFlags.Tool));
        Assert.Equal(1, multitool.ToolTier);
        Assert.True(content.Items.TryGet("core:build_tool", out var builder));
        Assert.True(builder.Flags.HasFlag(ItemFlags.Tool));
        foreach (var key in new[] { "core:stone", "core:soil", "core:sand", "core:ore" })
        {
            Assert.True(content.Items.TryGet(key, out var resource), key);
            Assert.Equal(500, resource.MaxStack);
            Assert.Equal(ItemCategory.Resource, resource.Category);
            Assert.True(resource.Flags.HasFlag(ItemFlags.Bulk));
        }

        Assert.Equal(
            [("core:stone", "core:stone", 2.0), ("core:dirt", "core:soil", 3.0), ("core:sand", "core:sand", 3.0), ("core:ore", "core:ore", 1.0)],
            content.Yields.Select(y => (y.MaterialKey, y.ItemKey, y.ItemsPerCubicMeter)));
        Assert.Equal(40, content.StartKit.BagSize);
        Assert.Equal(10, content.StartKit.HotbarSize);
        Assert.Equal(7, content.StartKit.EquipmentSize);
        Assert.Equal(
            [new StartKitEntry("core:multitool", 1, ContainerKind.Hotbar, 0), new StartKitEntry("core:build_tool", 1, ContainerKind.Hotbar, 1)],
            content.StartKit.Entries);
    }

    [Fact]
    public void Parse_TestContent_IsValid()
    {
        var content = SurvivalContentFactory.CreateTest();

        Assert.Equal(SurvivalContentFactory.ItemKeys, content.Items.Items.Select(i => i.Key));
        Assert.Equal([1, 5, 500, 500], content.Items.Items.Select(i => (int)i.MaxStack));
        Assert.True(content.TryGetYield("stone", out var yield));
        Assert.Equal(SurvivalContentFactory.StoneKey, yield.ItemKey);
        Assert.False(content.TryGetYield("bedrock", out _));
    }

    [Fact]
    public void Parse_DuplicateKey_Throws()
    {
        const string Duplicate = """
            { "items": [
              { "key": "test:tool", "category": "tool", "flags": ["tool"] },
              { "key": "test:stone", "category": "resource" },
              { "key": "test:stone", "category": "resource" } ] }
            """;

        AssertInvalid("$.items[2].key", () => ContentLoader.Parse(Duplicate, Yields, Start));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(-1)]
    public void Parse_MaxStackOutOfRange_Throws(int maxStack)
    {
        var items = Items.Replace("\"category\": \"resource\"", $"\"category\": \"resource\", \"maxStack\": {maxStack}, \"maxStackOverride\": true", StringComparison.Ordinal);

        AssertInvalid("$.items[1].maxStack", () => ContentLoader.Parse(items, Yields, Start));
    }

    [Fact]
    public void Parse_MaxStackAgainstCategoryWithoutOverride_Throws()
    {
        var items = Items.Replace("\"category\": \"tool\"", "\"category\": \"tool\", \"maxStack\": 5", StringComparison.Ordinal);

        AssertInvalid("$.items[0].maxStack", () => ContentLoader.Parse(items, Yields, Start));
        Assert.Equal(5, ContentLoader.Parse(items.Replace("\"maxStack\": 5", "\"maxStack\": 5, \"maxStackOverride\": true", StringComparison.Ordinal), Yields, Start)
            .Items.Items[0].MaxStack);
    }

    [Fact]
    public void Parse_UnknownCategoryOrFlag_Throws()
    {
        AssertInvalid("$.items[0].category", () => ContentLoader.Parse(Items.Replace("\"tool\", \"flags\"", "\"weird\", \"flags\"", StringComparison.Ordinal), Yields, Start));
        AssertInvalid("$.items[1].flags[0]", () => ContentLoader.Parse(Items.Replace("[\"bulk\"]", "[\"1\"]", StringComparison.Ordinal), Yields, Start));
    }

    [Fact]
    public void Parse_YieldUnknownItem_Throws()
    {
        var yields = Yields.Replace("test:stone", "test:nothing", StringComparison.Ordinal);

        AssertInvalid("$.yields[0].item", () => ContentLoader.Parse(Items, yields, Start));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("\"x\"")]
    public void Parse_YieldBadRate_Throws(string rate)
    {
        var yields = Yields.Replace("\"itemsPerCubicMeter\": 2", $"\"itemsPerCubicMeter\": {rate}", StringComparison.Ordinal);

        AssertInvalid("$.yields[0].itemsPerCubicMeter", () => ContentLoader.Parse(Items, yields, Start));
    }

    [Fact]
    public void Parse_StartKitSlotOutOfRange_Throws()
    {
        var start = Start.Replace("\"slot\": 0", "\"slot\": 10", StringComparison.Ordinal);

        AssertInvalid("$.items[0].slot", () => ContentLoader.Parse(Items, Yields, start));
    }

    [Fact]
    public void Parse_StartKitUnknownItemOrOverStack_Throws()
    {
        AssertInvalid("$.items[0].item", () => ContentLoader.Parse(Items, Yields, Start.Replace("\"item\": \"test:tool\"", "\"item\": \"test:x\"", StringComparison.Ordinal)));
        AssertInvalid("$.items[0].count", () => ContentLoader.Parse(Items, Yields, Start.Replace("\"count\": 1", "\"count\": 2", StringComparison.Ordinal)));
        AssertInvalid("$.containers.hotbar", () => ContentLoader.Parse(Items, Yields, Start.Replace("\"hotbar\": 10", "\"hotbar\": 0", StringComparison.Ordinal)));
    }

    [Fact]
    public void Parse_ItemWithoutSource_Throws()
    {
        var items = Items.Replace("} ] }", "}, { \"key\": \"test:orphan\", \"category\": \"resource\" } ] }", StringComparison.Ordinal);

        AssertInvalid("$.items[2]", () => ContentLoader.Parse(items, Yields, Start));
        var retired = items.Replace("\"key\": \"test:orphan\", \"category\": \"resource\"", "\"key\": \"test:orphan\", \"category\": \"resource\", \"flags\": [\"retired\"]", StringComparison.Ordinal);
        Assert.Equal(3, ContentLoader.Parse(retired, Yields, Start).Items.Items.Count);
    }

    [Fact]
    public void Parse_MalformedJson_Throws()
    {
        Assert.Throws<InvalidDataException>(() => ContentLoader.Parse("{ \"items\": [", Yields, Start));
        AssertInvalid("$.items", () => ContentLoader.Parse("{ }", Yields, Start));
    }

    [Fact]
    public void CheckWorldRegistry_RemovedType_Throws()
    {
        var content = SurvivalContentFactory.CreateTest();

        content.Items.CheckWorldItemTypes(SurvivalContentFactory.ItemKeys);
        var exception = Assert.Throws<InvalidDataException>(() => content.Items.CheckWorldItemTypes([.. SurvivalContentFactory.ItemKeys, "test:gone"]));
        Assert.Contains("test:gone", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertInvalid(string path, Action parse)
    {
        var exception = Assert.Throws<InvalidDataException>(parse);
        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
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
