using Horyzont.Core.Content;
using Horyzont.Core.Survival;

namespace Horyzont.Core.Tests.Content;

/// <summary>
/// The fields <c>suitRefill</c> and <c>suitModule</c> of <c>items.json</c> (K6-34 D8). <c>suitRefill</c>: <c>{ "resource", "amount" }</c> with the resource one of heating, cooling, filter, shield, oxygen and the amount
/// 1 to 1000; it excludes <c>food</c>. <c>suitModule</c>: <c>{ "effects": [ { "resource", "capacity", "drain" } ] }</c> with 1 to 5 effects of different resources, a capacity of 0 to 500 and a drain of 0.1 to 1.0
/// (finite), no effect that changes nothing (capacity 0 and drain 1); the field goes with <c>"equipmentSlot": "module"</c> and with nothing else, and a module is a stack of one. Every error is an
/// <see cref="InvalidDataException"/> naming the file and the JSON path; every refusal test first parses the valid twin. Until step B3 the loader ignores the fields, so these tests fail on it.
/// </summary>
public sealed class SuitContentTests
{
    private const string Yields = """{ "yields": [ { "material": "stone", "item": "test:stone", "itemsPerCubicMeter": 2, "toolTier": 1 } ] }""";

    private const string Start = """
        { "containers": { "playerBag": 40, "hotbar": 10, "equipment": 7 },
          "items": [ { "item": "test:tool", "count": 1, "container": "hotbar", "slot": 0 },
                     { "item": "test:canister", "count": 1, "container": "hotbar", "slot": 1 },
                     { "item": "test:tank", "count": 1, "container": "hotbar", "slot": 2 } ] }
        """;

    private const string Tool = """{ "key": "test:tool", "category": "tool", "flags": ["tool"], "toolTier": 1 }""";

    private const string Stone = """{ "key": "test:stone", "category": "resource", "flags": ["bulk"] }""";

    private const string ValidRefill = """{ "resource": "oxygen", "amount": 50 }""";

    private static readonly string[] _resourceNames = ["heating", "cooling", "filter", "shield", "oxygen"];

    private const string ValidModule = """{ "effects": [ { "resource": "oxygen", "capacity": 100, "drain": 1.0 } ] }""";

    private static string Canister(string? refill, string extra = "") =>
        "{ \"key\": \"test:canister\", \"category\": \"component\"" + extra + (refill is null ? string.Empty : ", \"suitRefill\": " + refill) + " }";

    private static string Tank(string? module, string slot = ", \"equipmentSlot\": \"module\"", string extra = "") =>
        "{ \"key\": \"test:tank\", \"category\": \"armor\", \"flags\": [\"armor\"]" + slot + extra + (module is null ? string.Empty : ", \"suitModule\": " + module) + " }";

    private static string Items(string canister, string tank) => "{ \"items\": [ " + Tool + ", " + Stone + ", " + canister + ", " + tank + " ] }";

    private static SurvivalContent Parse(string canister, string tank) => ContentLoader.Parse(Items(canister, tank), Yields, Start);

    private static void AssertInvalid(string path, Action parse)
    {
        var e = Assert.Throws<InvalidDataException>(parse);
        Assert.Contains(ContentLoader.ItemsFile, e.Message, StringComparison.Ordinal);
        Assert.Contains(path, e.Message, StringComparison.Ordinal);
    }

    private static void AssertTwin() => Assert.NotNull(Parse(Canister(ValidRefill), Tank(ValidModule)).Items.Items.Single(i => i.Key == "test:canister").SuitRefill);

    [Fact]
    public void Parse_SuitRefill()
    {
        var content = Parse(Canister(ValidRefill), Tank(ValidModule));

        Assert.True(content.Items.TryGet("test:canister", out var canister));
        Assert.Equal(new SuitRefillDef(SuitResource.Oxygen, 50), canister.SuitRefill);
        Assert.Null(canister.SuitModule);
        Assert.True(content.Items.TryGet("test:tank", out var tank));
        Assert.Null(tank.SuitRefill);
        var module = Assert.IsType<SuitModuleDef>(tank.SuitModule);
        Assert.Equal(new SuitModuleEffect(SuitResource.Oxygen, 100, 1.0f), Assert.Single(module.Effects));
        Assert.Equal(EquipmentSlot.Module, tank.EquipmentSlot);
        Assert.True(content.Items.TryGet("test:stone", out var stone));
        Assert.Null(stone.SuitRefill);
        Assert.Null(stone.SuitModule);
    }

    [Theory]
    [InlineData("heating", SuitResource.Heating)]
    [InlineData("cooling", SuitResource.Cooling)]
    [InlineData("filter", SuitResource.Filter)]
    [InlineData("shield", SuitResource.Shield)]
    [InlineData("oxygen", SuitResource.Oxygen)]
    public void Parse_EveryResourceName(string name, SuitResource resource)
    {
        var content = Parse(Canister("{ \"resource\": \"" + name + "\", \"amount\": 1000 }"), Tank(ValidModule));

        Assert.True(content.Items.TryGet("test:canister", out var canister));
        Assert.Equal(new SuitRefillDef(resource, 1000), canister.SuitRefill);
    }

    [Theory]
    [InlineData("{ \"resource\": \"oxygen\", \"amount\": 0 }", "$.items[2].suitRefill.amount")]
    [InlineData("{ \"resource\": \"oxygen\", \"amount\": 1001 }", "$.items[2].suitRefill.amount")]
    [InlineData("{ \"resource\": \"oxygen\", \"amount\": 12.5 }", "$.items[2].suitRefill.amount")]
    [InlineData("{ \"resource\": \"oxygen\" }", "$.items[2].suitRefill.amount")]
    [InlineData("{ \"resource\": \"air\", \"amount\": 50 }", "$.items[2].suitRefill.resource")]
    [InlineData("{ \"amount\": 50 }", "$.items[2].suitRefill.resource")]
    [InlineData("[]", "$.items[2].suitRefill")]
    public void Parse_RefillOutOfRange_Throws(string refill, string path)
    {
        AssertTwin();

        AssertInvalid(path, () => Parse(Canister(refill), Tank(ValidModule)));
    }

    [Fact]
    public void Parse_RefillAndFood_Throws()
    {
        AssertTwin();

        // A refill is no food: the flag and the object of the food are refused beside it (and the food rules do not apply to the refill).
        var food = Canister(ValidRefill, ", \"flags\": [\"food\"], \"food\": { \"health\": 20, \"stamina\": 30, \"regen\": 1.0, \"durationSeconds\": 1200 }")
            .Replace("\"category\": \"component\"", "\"category\": \"food\"", StringComparison.Ordinal);
        AssertInvalid("$.items[2].suitRefill", () => Parse(food, Tank(ValidModule)));
    }

    [Fact]
    public void Parse_ModuleWithoutModuleSlot_Throws()
    {
        AssertTwin();

        AssertInvalid("$.items[3].suitModule", () => Parse(Canister(ValidRefill), Tank(ValidModule, slot: string.Empty)));
        AssertInvalid("$.items[3].suitModule", () => Parse(Canister(ValidRefill), Tank(ValidModule, slot: ", \"equipmentSlot\": \"helmet\"")));

        // A module on an item that is no armor of a slot at all (the canister has none).
        AssertInvalid("$.items[2].suitModule", () => Parse(Canister(ValidRefill, ", \"suitModule\": " + ValidModule), Tank(ValidModule)));
    }

    [Fact]
    public void Parse_ModuleSlotWithoutModule_Throws()
    {
        AssertTwin();

        // An item for a module slot is useless without the effects; every other equipment slot may be worn without a module field (no armor effect before K7-42).
        AssertInvalid("$.items[3].suitModule", () => Parse(Canister(ValidRefill), Tank(null)));
        Assert.NotNull(Parse(Canister(ValidRefill), Tank(null, slot: ", \"equipmentSlot\": \"helmet\"")));
    }

    [Theory]
    [InlineData("{ \"resource\": \"oxygen\", \"capacity\": 501, \"drain\": 1.0 }", "$.items[3].suitModule.effects[0].capacity")]
    [InlineData("{ \"resource\": \"oxygen\", \"capacity\": -1, \"drain\": 0.5 }", "$.items[3].suitModule.effects[0].capacity")]
    [InlineData("{ \"resource\": \"oxygen\", \"capacity\": 1.5, \"drain\": 0.5 }", "$.items[3].suitModule.effects[0].capacity")]
    [InlineData("{ \"resource\": \"oxygen\", \"capacity\": 10, \"drain\": 0.09 }", "$.items[3].suitModule.effects[0].drain")]
    [InlineData("{ \"resource\": \"oxygen\", \"capacity\": 10, \"drain\": 1.01 }", "$.items[3].suitModule.effects[0].drain")]
    [InlineData("{ \"resource\": \"oxygen\", \"capacity\": 10, \"drain\": \"low\" }", "$.items[3].suitModule.effects[0].drain")]
    [InlineData("{ \"resource\": \"oxygen\", \"drain\": 0.5 }", "$.items[3].suitModule.effects[0].capacity")]
    [InlineData("{ \"resource\": \"oxygen\", \"capacity\": 10 }", "$.items[3].suitModule.effects[0].drain")]
    [InlineData("{ \"resource\": \"fire\", \"capacity\": 10, \"drain\": 0.5 }", "$.items[3].suitModule.effects[0].resource")]
    public void Parse_ModuleEffectOutOfRange_Throws(string effect, string path)
    {
        AssertTwin();

        AssertInvalid(path, () => Parse(Canister(ValidRefill), Tank("{ \"effects\": [ " + effect + " ] }")));
    }

    [Fact]
    public void Parse_ModuleEffectCount_Throws()
    {
        const string one = "{ \"resource\": \"oxygen\", \"capacity\": 10, \"drain\": 0.5 }";
        var five = string.Join(", ", _resourceNames.Select(r => "{ \"resource\": \"" + r + "\", \"capacity\": 10, \"drain\": 0.5 }"));
        AssertTwin();
        Assert.NotNull(Parse(Canister(ValidRefill), Tank("{ \"effects\": [ " + five + " ] }")));

        AssertInvalid("$.items[3].suitModule.effects", () => Parse(Canister(ValidRefill), Tank("{ \"effects\": [] }")));
        AssertInvalid("$.items[3].suitModule.effects", () => Parse(Canister(ValidRefill), Tank("{ \"effects\": [ " + five + ", " + one + " ] }")));
        AssertInvalid("$.items[3].suitModule.effects", () => Parse(Canister(ValidRefill), Tank("{ }")));
    }

    [Fact]
    public void Parse_ModuleNeutralEffect_Throws()
    {
        AssertTwin();

        // Capacity 0 with drain 1 changes nothing: a module of it is a mistake of the data.
        AssertInvalid(
            "$.items[3].suitModule.effects[0]",
            () => Parse(Canister(ValidRefill), Tank("{ \"effects\": [ { \"resource\": \"oxygen\", \"capacity\": 0, \"drain\": 1.0 } ] }")));
        Assert.NotNull(Parse(Canister(ValidRefill), Tank("{ \"effects\": [ { \"resource\": \"oxygen\", \"capacity\": 0, \"drain\": 0.9 } ] }")));
        Assert.NotNull(Parse(Canister(ValidRefill), Tank("{ \"effects\": [ { \"resource\": \"oxygen\", \"capacity\": 1, \"drain\": 1.0 } ] }")));
    }

    [Fact]
    public void Parse_DuplicateEffectResource_Throws()
    {
        const string oxygen = "{ \"resource\": \"oxygen\", \"capacity\": 10, \"drain\": 0.5 }";
        const string heating = "{ \"resource\": \"heating\", \"capacity\": 10, \"drain\": 0.5 }";
        AssertTwin();
        Assert.NotNull(Parse(Canister(ValidRefill), Tank("{ \"effects\": [ " + oxygen + ", " + heating + " ] }")));

        AssertInvalid("$.items[3].suitModule.effects[1].resource", () => Parse(Canister(ValidRefill), Tank("{ \"effects\": [ " + oxygen + ", " + oxygen + " ] }")));
    }

    [Fact]
    public void Parse_ModuleStack_MustBeOne()
    {
        AssertTwin();

        // A module is worn once: the stack limit of the armor is 1 and a larger one is refused for a module.
        AssertInvalid(
            "$.items[3].maxStack",
            () => Parse(Canister(ValidRefill), Tank(ValidModule, extra: ", \"maxStack\": 5, \"maxStackOverride\": true")));
    }

    [Fact]
    public void RepoContent_RefillsAndModulesHaveSources()
    {
        var content = ContentLoader.LoadDirectory(Path.Combine(RepoRoot(), "game", "content"));

        // D24: one refill for each of the five resources and the modules (a tank, a thermal module, a filter module, a shielding module), every one made by a recipe (the loader refuses an item without a source).
        var refills = content.Items.Items.Where(i => i.SuitRefill is not null).ToArray();
        Assert.Equal(
            [SuitResource.Heating, SuitResource.Cooling, SuitResource.Filter, SuitResource.Shield, SuitResource.Oxygen],
            refills.Select(i => i.SuitRefill!.Resource).Distinct().OrderBy(r => (int)r));
        Assert.All(refills, i => Assert.InRange((int)i.SuitRefill!.Amount, 1, 1000));
        var modules = content.Items.Items.Where(i => i.SuitModule is not null).ToArray();
        Assert.True(modules.Length >= 4);
        Assert.All(modules, i => Assert.Equal((EquipmentSlot.Module, (ushort)1), (i.EquipmentSlot, i.MaxStack)));
        var results = content.Recipes.Select(r => r.Result.ItemKey).ToHashSet();
        Assert.All(refills.Concat(modules), i => Assert.Contains(i.Key, results));
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
