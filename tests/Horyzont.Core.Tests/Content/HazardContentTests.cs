using Horyzont.Core.Content;
using Horyzont.Core.Survival;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Content;

/// <summary>
/// <c>hazards/hazards.json</c> (K6-34 D8, D24): <c>profiles</c> (1 to 64, unique keys; for each of cold, heat, toxins, radiation and vacuum an object <c>{ "day", "night" }</c> of 0 to 10 points a second, a missing hazard
/// is 0; optional <c>zones</c> ascending by <c>minHeight</c> -10000 to 100000 with scales 0 to 10), <c>bodies</c> (<c>body</c> 0 to 8, no repeats, known profiles), <c>defaultProfile</c>, <c>spaceProfile</c> and
/// <c>oxygenGenerator</c> (<c>fuel</c> an existing item that is not retired, <c>secondsPerCell</c> 10 to 86 400). The file is optional: without it the content has no hazards. Every error is an
/// <see cref="InvalidDataException"/> naming the file and the JSON path; every refusal test first parses the valid twin. Until step B3 the loader does not read the file, so these tests fail on it.
/// </summary>
public sealed class HazardContentTests
{
    private const string Fuel = SurvivalContentFactory.FuelCellKey;

    private static string Profiles(string temperate = """{ "key": "core:temperate", "cold": { "day": 0, "night": 0.25 }, "zones": [ { "minHeight": 150, "scale": { "cold": 2 } } ] }""") =>
        "[ " + temperate + """, { "key": "core:moon", "heat": { "day": 0.3, "night": 0 }, "radiation": { "day": 0.3, "night": 0 }, "vacuum": { "day": 0.5, "night": 0.5 } }, """
        + """{ "key": "core:space", "cold": { "day": 0.3, "night": 0.3 }, "vacuum": { "day": 0.5, "night": 0.5 } } ]""";

    private static string Hazards(
        string? profiles = null, string bodies = """[ { "body": 0, "profile": "core:temperate" }, { "body": 1, "profile": "core:moon" } ]""",
        string defaultProfile = "core:temperate", string spaceProfile = "core:space", string? generator = null) =>
        "{ \"formatVersion\": 1, \"profiles\": " + (profiles ?? Profiles()) + ", \"bodies\": " + bodies + ", \"defaultProfile\": \"" + defaultProfile + "\", \"spaceProfile\": \""
        + spaceProfile + "\", \"oxygenGenerator\": " + (generator ?? "{ \"fuel\": \"" + Fuel + "\", \"secondsPerCell\": 300 }") + " }";

    private static SurvivalContent Parse(string hazards) =>
        ContentLoader.Parse(SurvivalContentFactory.CraftingItemsJson, SurvivalContentFactory.CraftingYieldsJson, SurvivalContentFactory.StartJson, null, null, hazards);

    private static void AssertInvalid(string path, string hazards)
    {
        var e = Assert.Throws<InvalidDataException>(() => Parse(hazards));
        Assert.Contains(ContentLoader.HazardsFile, e.Message, StringComparison.Ordinal);
        Assert.Contains(path, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Profiles()
    {
        var content = Parse(Hazards());

        Assert.True(content.HasHazards);
        var hazards = Assert.IsType<HazardContent>(content.Hazards);
        Assert.Equal(["core:temperate", "core:moon", "core:space"], hazards.Profiles.Select(p => p.Key));
        Assert.True(hazards.TryGetProfile("core:temperate", out var temperate));
        Assert.Equal(new HazardRates(0f, 0f, 0f, 0f, 0f), temperate.Day);
        Assert.Equal(new HazardRates(0.25f, 0f, 0f, 0f, 0f), temperate.Night);
        var zone = Assert.Single(temperate.Zones);
        Assert.Equal(150f, zone.MinHeight);

        // A hazard a zone does not name is scaled by 1.
        Assert.Equal(new HazardRates(2f, 1f, 1f, 1f, 1f), zone.Scale);
        Assert.True(hazards.TryGetProfile("core:moon", out var moon));
        Assert.Equal(new HazardRates(0f, 0.3f, 0f, 0.3f, 0.5f), moon.Day);
        Assert.Equal(new HazardRates(0f, 0f, 0f, 0f, 0.5f), moon.Night);
        Assert.Equal(new[] { new HazardBodyDef(0, "core:temperate"), new HazardBodyDef(1, "core:moon") }, hazards.Bodies);
        Assert.Equal("core:temperate", hazards.DefaultProfile);
        Assert.Equal("core:space", hazards.SpaceProfile);
        Assert.Equal(new OxygenGeneratorDef(Fuel, 300), hazards.Generator);
        Assert.False(hazards.TryGetProfile("core:nothing", out _));
    }

    [Fact]
    public void Parse_MissingFile_NoHazards()
    {
        var content = ContentLoader.Parse(SurvivalContentFactory.CraftingItemsJson, SurvivalContentFactory.CraftingYieldsJson, SurvivalContentFactory.StartJson);

        Assert.False(content.HasHazards);
        Assert.Null(content.Hazards);
    }

    [Theory]
    [InlineData("""{ "day": 10, "night": 0 }""", true)]
    [InlineData("""{ "day": 0, "night": 10 }""", true)]
    [InlineData("""{ "day": 10.01, "night": 0 }""", false)]
    [InlineData("""{ "day": -0.1, "night": 0 }""", false)]
    [InlineData("""{ "day": 1, "night": 11 }""", false)]
    [InlineData("""{ "day": "high", "night": 0 }""", false)]
    [InlineData("""{ "day": 1 }""", false)]
    [InlineData("""[]""", false)]
    public void Parse_RateOutOfRange_Throws(string rate, bool valid)
    {
        var twin = Hazards(Profiles("""{ "key": "core:temperate", "cold": { "day": 0, "night": 0.25 } }"""));
        Assert.True(Parse(twin).HasHazards);

        var profile = "{ \"key\": \"core:temperate\", \"cold\": " + rate + " }";
        if (valid)
        {
            Assert.True(Parse(Hazards(Profiles(profile))).HasHazards);
        }
        else
        {
            AssertInvalid("$.profiles[0].cold", Hazards(Profiles(profile)));
        }
    }

    [Fact]
    public void Parse_ZonesNotAscending_Throws()
    {
        static string Temperate(string zones) => "{ \"key\": \"core:temperate\", \"zones\": " + zones + " }";
        Assert.True(Parse(Hazards(Profiles(Temperate("""[ { "minHeight": 0, "scale": { "cold": 1 } }, { "minHeight": 150, "scale": { "cold": 2 } } ]""")))).HasHazards);

        AssertInvalid("$.profiles[0].zones[1].minHeight", Hazards(Profiles(Temperate("""[ { "minHeight": 150, "scale": { "cold": 2 } }, { "minHeight": 150, "scale": { "cold": 1 } } ]"""))));
        AssertInvalid("$.profiles[0].zones[1].minHeight", Hazards(Profiles(Temperate("""[ { "minHeight": 150, "scale": { "cold": 2 } }, { "minHeight": 10, "scale": { "cold": 1 } } ]"""))));
        AssertInvalid("$.profiles[0].zones[0].minHeight", Hazards(Profiles(Temperate("""[ { "minHeight": 100001, "scale": { "cold": 2 } } ]"""))));
        AssertInvalid("$.profiles[0].zones[0].minHeight", Hazards(Profiles(Temperate("""[ { "minHeight": -10001, "scale": { "cold": 2 } } ]"""))));
        AssertInvalid("$.profiles[0].zones[0].scale.cold", Hazards(Profiles(Temperate("""[ { "minHeight": 0, "scale": { "cold": 10.5 } } ]"""))));
        AssertInvalid("$.profiles[0].zones[0].scale.cold", Hazards(Profiles(Temperate("""[ { "minHeight": 0, "scale": { "cold": -1 } } ]"""))));
    }

    [Fact]
    public void Parse_UnknownProfileInBodies_Throws()
    {
        Assert.True(Parse(Hazards(bodies: """[ { "body": 2, "profile": "core:moon" } ]""")).HasHazards);

        AssertInvalid("$.bodies[0].profile", Hazards(bodies: """[ { "body": 2, "profile": "core:lava" } ]"""));
        AssertInvalid("$.bodies[1].profile", Hazards(bodies: """[ { "body": 0, "profile": "core:moon" }, { "body": 1 } ]"""));
        AssertInvalid("$.bodies[0].body", Hazards(bodies: """[ { "body": 9, "profile": "core:moon" } ]"""));
        AssertInvalid("$.bodies[0].body", Hazards(bodies: """[ { "body": -1, "profile": "core:moon" } ]"""));
    }

    [Fact]
    public void Parse_DuplicateBody_Throws()
    {
        Assert.True(Parse(Hazards(bodies: """[ { "body": 0, "profile": "core:temperate" }, { "body": 1, "profile": "core:temperate" } ]""")).HasHazards);

        AssertInvalid("$.bodies[1].body", Hazards(bodies: """[ { "body": 1, "profile": "core:temperate" }, { "body": 1, "profile": "core:moon" } ]"""));
    }

    [Fact]
    public void Parse_DuplicateProfileKey_Throws()
    {
        var twin = """[ { "key": "core:temperate" }, { "key": "core:moon" }, { "key": "core:space" } ]""";
        Assert.True(Parse(Hazards(twin)).HasHazards);

        AssertInvalid("$.profiles[1].key", Hazards("""[ { "key": "core:temperate" }, { "key": "core:temperate" }, { "key": "core:space" } ]"""));
        AssertInvalid("$.profiles", Hazards("[]", bodies: "[]"));
    }

    [Fact]
    public void Parse_DefaultOrSpaceProfileMissing_Throws()
    {
        Assert.True(Parse(Hazards(defaultProfile: "core:moon", spaceProfile: "core:moon")).HasHazards);

        AssertInvalid("$.defaultProfile", Hazards(defaultProfile: "core:nothing"));
        AssertInvalid("$.spaceProfile", Hazards(spaceProfile: "core:nothing"));
        AssertInvalid("$.defaultProfile", Hazards(defaultProfile: string.Empty));
    }

    [Fact]
    public void Parse_GeneratorFuelUnknownOrRetired_Throws()
    {
        Assert.True(Parse(Hazards(generator: """{ "fuel": "test:peat", "secondsPerCell": 300 }""")).HasHazards);

        AssertInvalid("$.oxygenGenerator.fuel", Hazards(generator: """{ "fuel": "test:unknown", "secondsPerCell": 300 }"""));
        AssertInvalid("$.oxygenGenerator.fuel", Hazards(generator: """{ "secondsPerCell": 300 }"""));

        // A retired item exists in the registry but cannot be made any more: it is no fuel.
        var items = SurvivalContentFactory.CraftingItemsJson.Replace(
            "{ \"key\": \"test:module\", \"category\": \"resource\", \"flags\": [\"bulk\"] }",
            "{ \"key\": \"test:module\", \"category\": \"resource\", \"flags\": [\"bulk\"] }, { \"key\": \"test:old_cell\", \"category\": \"component\", \"flags\": [\"retired\"] }",
            StringComparison.Ordinal);
        Assert.Contains("test:old_cell", items, StringComparison.Ordinal);
        var e = Assert.Throws<InvalidDataException>(() => ContentLoader.Parse(
            items, SurvivalContentFactory.CraftingYieldsJson, SurvivalContentFactory.StartJson, null, null, Hazards(generator: """{ "fuel": "test:old_cell", "secondsPerCell": 300 }""")));
        Assert.Contains("$.oxygenGenerator.fuel", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10", true)]
    [InlineData("86400", true)]
    [InlineData("9", false)]
    [InlineData("86401", false)]
    [InlineData("0", false)]
    [InlineData("-300", false)]
    [InlineData("300.5", false)]
    [InlineData("\"300\"", false)]
    public void Parse_SecondsPerCellOutOfRange_Throws(string seconds, bool valid)
    {
        var json = Hazards(generator: "{ \"fuel\": \"" + Fuel + "\", \"secondsPerCell\": " + seconds + " }");
        if (valid)
        {
            Assert.True(Parse(json).HasHazards);
            return;
        }

        Assert.True(Parse(Hazards()).HasHazards);
        AssertInvalid("$.oxygenGenerator.secondsPerCell", json);
    }

    [Fact]
    public void Parse_WrongFormatVersion_Throws()
    {
        Assert.True(Parse(Hazards()).HasHazards);

        AssertInvalid("$.formatVersion", Hazards().Replace("\"formatVersion\": 1", "\"formatVersion\": 2", StringComparison.Ordinal));
    }

    [Fact]
    public void RepoContent_FourBodiesAndSpace()
    {
        var content = ContentLoader.LoadDirectory(Path.Combine(RepoRoot(), "game", "content"));

        // D24: bodies 0 to 3 have their own profiles; space has a vacuum that never stops; only the moon and space have a vacuum; the generator burns the fuel cell for 5 minutes.
        Assert.True(content.HasHazards);
        var hazards = content.Hazards!;
        string KeyOf(int body) => hazards.Bodies.Single(b => b.Body == body).Profile;
        Assert.Equal(4, hazards.Bodies.Count);
        Assert.Equal(4, new[] { KeyOf(0), KeyOf(1), KeyOf(2), KeyOf(3) }.Distinct().Count());
        Assert.Equal(KeyOf(0), hazards.DefaultProfile);
        Assert.True(hazards.TryGetProfile(hazards.SpaceProfile, out var space));
        Assert.True(space.Day.Vacuum > 0f && space.Night.Vacuum > 0f);
        Assert.True(hazards.TryGetProfile(KeyOf(1), out var moon));
        Assert.True(moon.Day.Vacuum > 0f && moon.Night.Vacuum > 0f);
        Assert.True(hazards.TryGetProfile(KeyOf(0), out var home));
        Assert.Equal(0f, home.Day.Vacuum + home.Night.Vacuum);
        Assert.True(hazards.TryGetProfile(KeyOf(2), out var toxic));
        Assert.True(toxic.Day.Toxins > 0f || toxic.Night.Toxins > 0f);
        Assert.True(hazards.TryGetProfile(KeyOf(3), out var frozen));
        Assert.True(frozen.Night.Cold > 0f);
        Assert.Equal(new OxygenGeneratorDef("core:fuel_cell", 300), hazards.Generator);
        Assert.True(content.Items.TryGet(hazards.Generator.Fuel, out _));
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
