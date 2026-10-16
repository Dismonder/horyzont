using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;

using Horyzont.Constructs.Content;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

namespace Horyzont.Constructs.Tests.Content;

public sealed class BlockTypeCatalogTests
{
    private const string Valid = """
        {
          "formatVersion": 2,
          "materials": [ { "key": "stone", "color": "#808080" }, { "key": "wood", "color": "#A67B4BCC" } ],
          "blocks": [
            { "key": "core:stone_block", "shape": "cube", "mass": 100, "material": "stone" },
            { "key": "core:wood_planks", "shape": "cube", "mass": 100, "material": "wood", "collision": false },
            { "key": "core:stone_slab", "shape": "mesh", "mesh": "builtin:slab", "mass": 100, "material": "stone" },
            { "key": "mod:custom/thing", "shape": "mesh", "mesh": "res://thing.obj", "mass": 100, "material": "wood" }
          ]
        }
        """;

    private static BlockTypeCatalog Parse(string json) => BlockTypeCatalog.Parse(Encoding.UTF8.GetBytes(json), new FakeResolver());

    private static string Block(string body) =>
        "{ \"formatVersion\": 2, \"materials\": [ { \"key\": \"stone\", \"color\": \"#808080\" } ], \"blocks\": [ " + body + " ] }";

    private static void AssertInvalid(string json, string path)
    {
        var e = Assert.Throws<InvalidDataException>(() => Parse(json));
        Assert.StartsWith(path, e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ \"formatVersion\": 2, \"materials\": [ { \"key\": \"stone\", \"color\": \"#808080\" } ], \"blocks\": [ { \"key\": \"core:stone\n\", \"shape\": \"cube\", \"mass\": 100, \"material\": \"stone\" } ] }")]
    [InlineData("{ \"formatVersion\": 2, \"materials\": [ { \"key\": \"stone\n\", \"color\": \"#808080\" } ], \"blocks\": [] }")]
    [InlineData("{ \"formatVersion\": 2, \"materials\": [ { \"key\": \"stone\", \"color\": \"#808080\n\" } ], \"blocks\": [] }")]
    public void Parse_TrailingNewlineInKeyOrColor_IsRejected(string json) =>
        Assert.Throws<InvalidDataException>(() => Parse(json));

    [Fact]
    public void Parse_ValidFile_ProducesCatalog()
    {
        var catalog = Parse(Valid);

        Assert.Equal(["core:stone_block", "core:wood_planks", "core:stone_slab", "mod:custom/thing"], catalog.BlockTypeKeys);
        Assert.Equal(5, catalog.Blocks.Count);
        Assert.Equal(BlockTypeRegistry.MissingKey, catalog.Blocks[^1].Def.Key);
        Assert.Same(catalog.Missing, catalog.Blocks[^1]);

        // Material ids: the built-in missing material is 1, content materials follow in content order.
        Assert.Equal(["core:missing", "stone", "wood"], catalog.Materials.Select(m => m.Key));
        Assert.Equal([1, 2, 3], catalog.Materials.Select(m => (int)m.Id));
        Assert.Equal(0x808080FFu, catalog.Materials[1].ColorRgba8);
        Assert.Equal(0xA67B4BCCu, catalog.Materials[2].ColorRgba8);

        Assert.True(catalog.TryGet("core:wood_planks", out var wood));
        Assert.Equal(new BlockTypeDef("core:wood_planks", BlockShape.Cube, 3, false, 100f, BlockFunction.None), wood.Def);
        Assert.Equal("BLOCK_WOOD_PLANKS", wood.NameKey);
        Assert.Null(wood.Mesh);
        Assert.True(catalog.TryGet("core:stone_slab", out var slab));
        Assert.Equal(BlockShape.Mesh, slab.Def.Shape);
        Assert.NotNull(slab.Mesh);
        Assert.True(catalog.TryGet("mod:custom/thing", out var custom));
        Assert.Equal("BLOCK_CUSTOM_THING", custom.NameKey);
        Assert.False(catalog.TryGet("core:nothing", out _));
        Assert.True(catalog.TryGet(BlockTypeRegistry.MissingKey, out var missing));
        Assert.True(missing.Def.HasCollision);
    }

    private static string FunctionBlock(string function) => Block(
        "{ \"key\": \"a:b\", \"shape\": \"cube\", \"mass\": 10, \"material\": \"stone\", \"function\": " + function + " }");

    [Fact]
    public void BlockCatalog_Format2_ParsesMassAndFunctions()
    {
        const string json = """
            {
              "formatVersion": 2,
              "materials": [ { "key": "stone", "color": "#808080" } ],
              "blocks": [
                { "key": "a:plain", "shape": "cube", "mass": 12.5, "material": "stone" },
                { "key": "a:cockpit", "shape": "cube", "mass": 400, "material": "stone", "function": { "kind": "cockpit" } },
                { "key": "a:seat", "shape": "cube", "mass": 80, "material": "stone", "function": { "kind": "seat", "role": "passenger" } },
                { "key": "a:nav", "shape": "cube", "mass": 250, "material": "stone", "function": { "kind": "seat", "role": "navigator" } },
                { "key": "a:engine", "shape": "cube", "mass": 600, "material": "stone", "function": { "kind": "engine", "thrust": 60000, "tier": 1 } },
                { "key": "a:stab", "shape": "cube", "mass": 300, "material": "stone", "function": { "kind": "stabilizer", "torque": 40000 } },
                { "key": "a:tank", "shape": "cube", "mass": 250, "material": "stone", "function": { "kind": "fuel_tank", "capacity": 1000 } },
                { "key": "a:cargo", "shape": "cube", "mass": 200, "material": "stone", "function": { "kind": "cargo", "tier": 2 } },
                { "key": "a:gear", "shape": "cube", "mass": 150, "material": "stone", "function": { "kind": "landing_gear" } },
                { "key": "a:pulse", "shape": "cube", "mass": 800, "material": "stone", "function": { "kind": "pulse_drive", "speedCap": 2000 } },
                { "key": "a:door", "shape": "cube", "mass": 200, "material": "stone", "function": { "kind": "door" } },
                { "key": "a:refuel", "shape": "cube", "mass": 1500, "material": "stone", "function": { "kind": "refuel_station" } }
              ]
            }
            """;
        var catalog = Parse(json);

        BlockTypeDef Def(string key) => catalog.TryGet(key, out var b) ? b.Def : throw new InvalidOperationException(key);
        Assert.Equal(12.5f, Def("a:plain").MassKg);
        Assert.Equal(BlockFunction.None, Def("a:plain").Function);
        Assert.Equal(BlockFunction.Cockpit, Def("a:cockpit").Function);
        Assert.Equal(BlockFunction.CreateSeat(SeatRole.Passenger), Def("a:seat").Function);
        Assert.Equal(BlockFunction.CreateSeat(SeatRole.Navigator), Def("a:nav").Function);
        Assert.Equal(BlockFunction.CreateEngine(60000f, 1), Def("a:engine").Function);
        Assert.Equal(BlockFunction.CreateStabilizer(40000f), Def("a:stab").Function);
        Assert.Equal(BlockFunction.CreateFuelTank(1000f), Def("a:tank").Function);
        Assert.Equal(BlockFunction.CreateCargo(2), Def("a:cargo").Function);
        Assert.Equal(BlockFunction.LandingGear, Def("a:gear").Function);
        Assert.Equal(BlockFunction.CreatePulseDrive(2000f), Def("a:pulse").Function);
        Assert.Equal(BlockFunction.Door, Def("a:door").Function);
        Assert.Equal(BlockFunction.RefuelStation, Def("a:refuel").Function);
        Assert.Equal(BlockContentTable.MissingMassKg, catalog.Missing.Def.MassKg);
    }

    [Theory]
    [InlineData("{ \"kind\": \"warp\" }", "$.blocks[0].function.kind")]
    [InlineData("{ }", "$.blocks[0].function.kind")]
    [InlineData("[]", "$.blocks[0].function")]
    [InlineData("{ \"kind\": \"engine\", \"tier\": 1 }", "$.blocks[0].function.thrust")]
    [InlineData("{ \"kind\": \"engine\", \"thrust\": -5, \"tier\": 1 }", "$.blocks[0].function.thrust")]
    [InlineData("{ \"kind\": \"engine\", \"thrust\": 0, \"tier\": 1 }", "$.blocks[0].function.thrust")]
    [InlineData("{ \"kind\": \"engine\", \"thrust\": 1e60, \"tier\": 1 }", "$.blocks[0].function.thrust")]
    [InlineData("{ \"kind\": \"engine\", \"thrust\": 10, \"tier\": 0 }", "$.blocks[0].function.tier")]
    [InlineData("{ \"kind\": \"engine\", \"thrust\": 10, \"tier\": 1, \"torque\": 3 }", "$.blocks[0].function.torque")]
    [InlineData("{ \"kind\": \"cockpit\", \"thrust\": 3 }", "$.blocks[0].function.thrust")]
    [InlineData("{ \"kind\": \"seat\" }", "$.blocks[0].function.role")]
    [InlineData("{ \"kind\": \"seat\", \"role\": \"pilot\" }", "$.blocks[0].function.role")]
    [InlineData("{ \"kind\": \"fuel_tank\", \"capacity\": -1 }", "$.blocks[0].function.capacity")]
    [InlineData("{ \"kind\": \"pulse_drive\", \"speedCap\": \"fast\" }", "$.blocks[0].function.speedCap")]
    public void BlockCatalog_InvalidFunction_ThrowsWithJsonPath(string function, string path) => AssertInvalid(FunctionBlock(function), path);

    [Fact]
    public void Parse_RepoContent_HasChestBedLampDoor()
    {
        var catalog = BlockTypeCatalog.Parse(File.ReadAllBytes(Path.Combine(RepoRoot(), "game", "content", "blocks", "blocks.json")), new FakeResolver());

        // The tuning numbers (masses, slots, light) are data; the test pins only the shape of the content (K6-30 DECISION-11).
        var woodChest = Shipped(catalog, "core:chest_wood").Def;
        var metalChest = Shipped(catalog, "core:chest_metal").Def;
        var bed = Shipped(catalog, "core:bed").Def;
        var lamp = Shipped(catalog, "core:lamp");
        var door = Shipped(catalog, "core:wood_door").Def;

        Assert.Equal(BlockFunctionKind.Chest, woodChest.Function.Kind);
        Assert.Equal(BlockFunctionKind.Chest, metalChest.Function.Kind);
        Assert.InRange(woodChest.Function.ContainerSlots, 1, 256);
        Assert.InRange(metalChest.Function.ContainerSlots, 1, 256);
        Assert.True(metalChest.Function.ContainerSlots > woodChest.Function.ContainerSlots);
        Assert.Equal(BlockFunctionKind.Bed, bed.Function.Kind);
        Assert.Equal(BlockShape.Mesh, bed.Shape);
        Assert.Equal(BlockFunctionKind.Light, lamp.Def.Function.Kind);
        Assert.Equal(BlockFunctionKind.Door, door.Function.Kind);

        Assert.All([woodChest, metalChest, bed, lamp.Def, door], def => Assert.True(def.MassKg > 0f && float.IsFinite(def.MassKg)));
        Assert.NotNull(lamp.Light);
        Assert.True(lamp.Light.RangeMeters > 0f && float.IsFinite(lamp.Light.RangeMeters));
        Assert.True(lamp.Light.Energy > 0f && float.IsFinite(lamp.Light.Energy));
        Assert.Contains(catalog.Materials, m => m.Key == "lamp");
        Assert.All(["core:chest_wood", "core:chest_metal", "core:bed", "core:wood_door"], key => Assert.Null(Shipped(catalog, key).Light));

        // Every new type has a translation row.
        var csv = File.ReadAllLines(Path.Combine(RepoRoot(), "game", "locale", "blocks.csv"));
        var names = csv.Skip(1).Select(line => line.Split(',')[0]).ToHashSet();
        Assert.All(["core:chest_wood", "core:chest_metal", "core:bed", "core:lamp", "core:wood_door"], key => Assert.Contains(Shipped(catalog, key).NameKey, names));
    }

    private static BlockTypeContent Shipped(BlockTypeCatalog catalog, string key) =>
        catalog.TryGet(key, out var block) ? block : throw new InvalidOperationException($"{key} is not in blocks.json");

    private static byte MaterialId(BlockTypeCatalog catalog, string key) => catalog.Materials.Single(m => m.Key == key).Id;

    [Fact]
    public void Parse_ChestBedAndLightFunctions_AreRead()
    {
        static BlockTypeContent Content(string function) => Parse(FunctionBlock(function)).TryGet("a:b", out var b) ? b : throw new InvalidOperationException();

        Assert.Equal(BlockFunction.CreateChest(20), Content("{ \"kind\": \"chest\", \"slots\": 20 }").Def.Function);
        Assert.Equal(BlockFunction.CreateChest(256), Content("{ \"kind\": \"chest\", \"slots\": 256 }").Def.Function);
        Assert.Equal(BlockFunction.Bed, Content("{ \"kind\": \"bed\" }").Def.Function);
        var lamp = Content("{ \"kind\": \"light\", \"range\": 8, \"color\": \"#FFD9A0\", \"energy\": 1.5 }");
        Assert.Equal(BlockFunction.Light, lamp.Def.Function);
        Assert.Equal(new BlockLightDef(8f, 0xFFD9A0FFu, 1.5f), lamp.Light);
        Assert.Null(Content("{ \"kind\": \"bed\" }").Light);
    }

    [Fact]
    public void Parse_ChestWithoutSlots_Throws()
    {
        Parse(FunctionBlock("{ \"kind\": \"chest\", \"slots\": 4 }"));

        AssertInvalid(FunctionBlock("{ \"kind\": \"chest\" }"), "$.blocks[0].function.slots");
        AssertInvalid(FunctionBlock("{ \"kind\": \"chest\", \"slots\": 0 }"), "$.blocks[0].function.slots");
        AssertInvalid(FunctionBlock("{ \"kind\": \"chest\", \"slots\": 2.5 }"), "$.blocks[0].function.slots");
    }

    [Fact]
    public void Parse_ChestSlotsOver256_Throws()
    {
        Parse(FunctionBlock("{ \"kind\": \"chest\", \"slots\": 256 }"));

        AssertInvalid(FunctionBlock("{ \"kind\": \"chest\", \"slots\": 257 }"), "$.blocks[0].function.slots");
    }

    [Fact]
    public void Parse_LightWithInvalidColor_Throws()
    {
        Parse(FunctionBlock("{ \"kind\": \"light\", \"range\": 8, \"color\": \"#FFD9A0\", \"energy\": 1.5 }"));

        AssertInvalid(FunctionBlock("{ \"kind\": \"light\", \"range\": 8, \"color\": \"nope\", \"energy\": 1.5 }"), "$.blocks[0].function.color");
        AssertInvalid(FunctionBlock("{ \"kind\": \"light\", \"range\": 8, \"energy\": 1.5 }"), "$.blocks[0].function.color");
        AssertInvalid(FunctionBlock("{ \"kind\": \"light\", \"range\": 0, \"color\": \"#FFD9A0\", \"energy\": 1.5 }"), "$.blocks[0].function.range");
        AssertInvalid(FunctionBlock("{ \"kind\": \"light\", \"range\": 8, \"color\": \"#FFD9A0\", \"energy\": -1 }"), "$.blocks[0].function.energy");
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"mass\": 0, ")]
    [InlineData("\"mass\": -3, ")]
    [InlineData("\"mass\": \"heavy\", ")]
    [InlineData("\"mass\": 1e60, ")]
    public void BlockCatalog_MissingOrInvalidMass_Throws(string mass) =>
        AssertInvalid(Block("{ \"key\": \"a:b\", \"shape\": \"cube\", " + mass + "\"material\": \"stone\" }"), "$.blocks[0].mass");

    [Fact]
    public void ShippedContent_AllShipBlocksPresentWithMass()
    {
        var catalog = BlockTypeCatalog.Parse(File.ReadAllBytes(Path.Combine(RepoRoot(), "game", "content", "blocks", "blocks.json")), new FakeResolver());

        (string Key, float Mass, BlockFunction Function)[] expected =
        [
            ("core:stone_block", 2400f, BlockFunction.None),
            ("core:wood_planks", 500f, BlockFunction.None),
            ("core:metal_plate", 200f, BlockFunction.None),
            ("core:stone_slab", 1200f, BlockFunction.None),
            ("core:stone_slope", 1200f, BlockFunction.None),
            ("core:ship_cockpit", 400f, BlockFunction.Cockpit),
            ("core:ship_seat", 80f, BlockFunction.CreateSeat(SeatRole.Passenger)),
            ("core:ship_nav_station", 250f, BlockFunction.CreateSeat(SeatRole.Navigator)),
            ("core:ship_engine_t1", 600f, BlockFunction.CreateEngine(60000f, 1)),
            ("core:ship_stabilizer", 300f, BlockFunction.CreateStabilizer(40000f)),
            ("core:ship_fuel_tank", 250f, BlockFunction.CreateFuelTank(1000f)),
            ("core:ship_cargo", 200f, BlockFunction.CreateCargo(1)),
            ("core:ship_landing_gear", 150f, BlockFunction.LandingGear),
            ("core:ship_pulse_drive", 800f, BlockFunction.CreatePulseDrive(2000f)),
            ("core:ship_hull", 200f, BlockFunction.None),
            ("core:ship_window", 150f, BlockFunction.None),
            ("core:ship_door", 200f, BlockFunction.Door),
            ("core:ship_refuel_station", 1500f, BlockFunction.RefuelStation),
        ];
        foreach (var (key, mass, function) in expected)
        {
            Assert.True(catalog.TryGet(key, out var block), key);
            Assert.Equal(mass, block.Def.MassKg);
            Assert.Equal(function, block.Def.Function);
        }

        Assert.Equal(expected.Length, catalog.BlockTypeKeys.Count);
        Assert.Equal(BlockShape.Mesh, catalog.Blocks.Single(b => b.Def.Key == "core:ship_seat").Def.Shape);
    }

    [Fact]
    public void ShippedContent_FixtureDefinitionsMatchBlocksJson()
    {
        var catalog = BlockTypeCatalog.Parse(File.ReadAllBytes(Path.Combine(RepoRoot(), "game", "content", "blocks", "blocks.json")), new FakeResolver());

        Assert.Equal(ShipFixtures.BlockKeys, catalog.BlockTypeKeys);
        foreach (var block in catalog.Blocks)
        {
            var fixture = ShipFixtures.Def(block.Def.Key);
            Assert.Equal(block.Def.MassKg, fixture.MassKg);
            Assert.Equal(block.Def.Function, fixture.Function);
        }
    }

    [Fact]
    public void ContentTableBuilder_MapsRegistryIdsAndFallsBackToMissing()
    {
        var catalog = Parse(Valid);
        var state = WorldState.CreateNew(
            WorldFactory.DefaultWorldId, 1, [WorldFactory.HomePlanetParams], ["core:wood_planks", "mod:unknown", BlockTypeRegistry.MissingKey], ["stone"]);

        var table = BlockContentTableBuilder.Build(catalog, state.BlockTypes);

        Assert.True(state.BlockTypes.TryGetId("core:wood_planks", out var wood));
        Assert.Equal("core:wood_planks", table.Get(wood).Key);
        Assert.True(state.BlockTypes.TryGetId("mod:unknown", out var unknown));
        Assert.Equal(BlockTypeRegistry.MissingKey, table.Get(unknown).Key);
        Assert.Equal(BlockTypeRegistry.MissingKey, table.Get(0).Key);
        Assert.Equal(BlockTypeRegistry.MissingKey, table.Get(999).Key);
    }

    [Fact]
    public void Parse_InvalidJson_Throws() => AssertInvalid("{ not json", "$:");

    [Theory]
    [InlineData("[]", "$")]
    [InlineData("{ \"materials\": [], \"blocks\": [] }", "$.formatVersion")]
    [InlineData("{ \"formatVersion\": 1, \"materials\": [], \"blocks\": [] }", "$.formatVersion")]
    [InlineData("{ \"formatVersion\": 2, \"blocks\": [] }", "$.materials")]
    [InlineData("{ \"formatVersion\": 2, \"materials\": [] }", "$.blocks")]
    public void Parse_BadRoot_ThrowsWithPath(string json, string path) => AssertInvalid(json, path);

    [Theory]
    [InlineData("{ \"key\": \"nocolon\", \"shape\": \"cube\", \"mass\": 100, \"material\": \"stone\" }", "$.blocks[0].key")]
    [InlineData("{ \"key\": \"Core:Upper\", \"shape\": \"cube\", \"mass\": 100, \"material\": \"stone\" }", "$.blocks[0].key")]
    [InlineData("{ \"key\": \"core:missing\", \"shape\": \"cube\", \"mass\": 100, \"material\": \"stone\" }", "$.blocks[0].key")]
    [InlineData("{ \"shape\": \"cube\", \"mass\": 100, \"material\": \"stone\" }", "$.blocks[0].key")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"sphere\", \"mass\": 100, \"material\": \"stone\" }", "$.blocks[0].shape")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"cube\", \"mass\": 100, \"material\": \"lava\" }", "$.blocks[0].material")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"cube\", \"mass\": 100, \"material\": \"core:missing\" }", "$.blocks[0].material")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"mesh\", \"mass\": 100, \"material\": \"stone\" }", "$.blocks[0].mesh")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"cube\", \"mesh\": \"builtin:slab\", \"mass\": 100, \"material\": \"stone\" }", "$.blocks[0].mesh")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"mesh\", \"mesh\": \"builtin:unknown\", \"mass\": 100, \"material\": \"stone\" }", "$.blocks[0].mesh")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"mesh\", \"mesh\": \"res://bad_index.obj\", \"mass\": 100, \"material\": \"stone\" }", "$.blocks[0].mesh")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"mesh\", \"mesh\": \"res://bad_range.obj\", \"mass\": 100, \"material\": \"stone\" }", "$.blocks[0].mesh")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"mesh\", \"mesh\": \"res://bad_count.obj\", \"mass\": 100, \"material\": \"stone\" }", "$.blocks[0].mesh")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"mesh\", \"mesh\": \"res://nan.obj\", \"mass\": 100, \"material\": \"stone\" }", "$.blocks[0].mesh")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"cube\", \"mass\": 100, \"material\": \"stone\", \"collision\": 1 }", "$.blocks[0].collision")]
    public void Parse_BadBlock_ThrowsWithPath(string block, string path) => AssertInvalid(Block(block), path);

    [Fact]
    public void Parse_DuplicateKey_Throws() =>
        AssertInvalid(
            Block("{ \"key\": \"a:b\", \"shape\": \"cube\", \"mass\": 100, \"material\": \"stone\" }, { \"key\": \"a:b\", \"shape\": \"cube\", \"mass\": 100, \"material\": \"stone\" }"),
            "$.blocks[1].key");

    [Fact]
    public void Parse_KeyLongerThanProtocolLimit_Throws() =>
        AssertInvalid(Block($"{{ \"key\": \"a:{new string('b', 70)}\", \"shape\": \"cube\", \"mass\": 100, \"material\": \"stone\" }}"), "$.blocks[0].key");

    [Theory]
    [InlineData("{ \"key\": \"bad key\", \"color\": \"#000000\" }", "$.materials[0].key")]
    [InlineData("{ \"key\": \"x\", \"color\": \"red\" }", "$.materials[0].color")]
    [InlineData("{ \"key\": \"x\" }", "$.materials[0].color")]
    public void Parse_BadMaterial_ThrowsWithPath(string material, string path) =>
        AssertInvalid("{ \"formatVersion\": 2, \"materials\": [ " + material + " ], \"blocks\": [] }", path);

    [Fact]
    public void Parse_DuplicateMaterial_Throws() =>
        AssertInvalid(
            "{ \"formatVersion\": 2, \"materials\": [ { \"key\": \"x\", \"color\": \"#000000\" }, { \"key\": \"x\", \"color\": \"#000000\" } ], \"blocks\": [] }",
            "$.materials[1].key");

    [Fact]
    public void Parse_TooManyMaterials_Throws()
    {
        var materials = string.Join(",", Enumerable.Range(0, 255).Select(i => $"{{ \"key\": \"m{i}\", \"color\": \"#000000\" }}"));
        AssertInvalid("{ \"formatVersion\": 2, \"materials\": [" + materials + "], \"blocks\": [] }", "$.materials");
    }

    [Fact]
    public void Parse_TooManyBlockTypes_Throws()
    {
        var blocks = string.Join(",", Enumerable.Range(0, 2048).Select(i => $"{{ \"key\": \"t:b{i}\", \"shape\": \"cube\", \"mass\": 100, \"material\": \"stone\" }}"));
        AssertInvalid(Block(blocks), "$.blocks");
    }

    [Fact]
    public void Parse_MaxBlockTypes_IsAccepted()
    {
        var blocks = string.Join(",", Enumerable.Range(0, 2047).Select(i => $"{{ \"key\": \"t:b{i}\", \"shape\": \"cube\", \"mass\": 100, \"material\": \"stone\" }}"));
        Assert.Equal(2047, Parse(Block(blocks)).BlockTypeKeys.Count);
    }

    [Fact]
    public void RepositoryBlocksJson_Parses()
    {
        var path = Path.Combine(RepoRoot(), "game", "content", "blocks", "blocks.json");
        var catalog = BlockTypeCatalog.Parse(File.ReadAllBytes(path), new FakeResolver());

        Assert.Contains("core:metal_plate", catalog.BlockTypeKeys);
        Assert.True(catalog.TryGet("core:stone_slab", out var slab));
        Assert.Equal(BlockShape.Mesh, slab.Def.Shape);

        // Every block type has a translation row in game/locale/blocks.csv.
        var csv = File.ReadAllLines(Path.Combine(RepoRoot(), "game", "locale", "blocks.csv"));
        var names = csv.Skip(1).Select(line => line.Split(',')[0]).ToHashSet();
        Assert.All(catalog.Blocks, b => Assert.Contains(b.NameKey, names));
    }

    [Theory]
    [InlineData(BuiltInShapeMeshes.SlabRef)]
    [InlineData(BuiltInShapeMeshes.SlopeRef)]
    public void BuiltInShapes_AreValid_AndWindClockwiseOutward(string meshRef)
    {
        Assert.True(BuiltInShapeMeshes.TryResolve(meshRef, out var mesh));
        Assert.Null(mesh.Validate());
        var positions = mesh.Positions.ToArray();
        var center = (positions.Aggregate(new Vector3(float.MaxValue), Vector3.Min) + positions.Aggregate(new Vector3(float.MinValue), Vector3.Max)) / 2;
        var normals = mesh.Normals.ToArray();
        var indices = mesh.Indices.ToArray();
        for (var i = 0; i < indices.Length; i += 3)
        {
            var a = positions[indices[i]];
            var b = positions[indices[i + 1]];
            var c = positions[indices[i + 2]];

            // Godot front faces are clockwise: the right-handed cross product of a clockwise triangle points inward.
            var geometric = Vector3.Cross(b - a, c - a);
            Assert.True(Vector3.Dot(geometric, normals[indices[i]]) < 0, "triangle winding must be clockwise seen from outside");
            Assert.True(Vector3.Dot(normals[indices[i]], ((a + b + c) / 3) - center) > 0, "normal must point away from the mesh centre");
        }

        Assert.False(BuiltInShapeMeshes.TryResolve("builtin:nope", out _));
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

    private sealed class FakeResolver : IShapeMeshResolver
    {
        public bool TryResolve(string meshRef, [NotNullWhen(true)] out ShapeMesh? mesh)
        {
            var quad = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) };
            var normals = new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ };
            var uvs = new Vector2[3];
            switch (meshRef)
            {
                case "res://thing.obj":
                    mesh = new ShapeMesh(quad, normals, uvs, [0, 1, 2]);
                    return true;
                case "res://bad_index.obj":
                    mesh = new ShapeMesh(quad, normals, uvs, [0, 1, 3]);
                    return true;
                case "res://bad_count.obj":
                    mesh = new ShapeMesh(quad, normals, uvs, [0, 1]);
                    return true;
                case "res://bad_range.obj":
                    mesh = new ShapeMesh([new Vector3(0, 0, 2), quad[1], quad[2]], normals, uvs, [0, 1, 2]);
                    return true;
                case "res://nan.obj":
                    mesh = new ShapeMesh([new Vector3(float.NaN, 0, 0), quad[1], quad[2]], normals, uvs, [0, 1, 2]);
                    return true;
                default:
                    mesh = null;
                    return false;
            }
        }
    }
}
