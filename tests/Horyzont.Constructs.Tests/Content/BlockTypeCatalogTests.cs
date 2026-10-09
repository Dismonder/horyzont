using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;

using Horyzont.Constructs.Content;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Constructs.Tests.Content;

public sealed class BlockTypeCatalogTests
{
    private const string Valid = """
        {
          "formatVersion": 1,
          "materials": [ { "key": "stone", "color": "#808080" }, { "key": "wood", "color": "#A67B4BCC" } ],
          "blocks": [
            { "key": "core:stone_block", "shape": "cube", "material": "stone" },
            { "key": "core:wood_planks", "shape": "cube", "material": "wood", "collision": false },
            { "key": "core:stone_slab", "shape": "mesh", "mesh": "builtin:slab", "material": "stone" },
            { "key": "mod:custom/thing", "shape": "mesh", "mesh": "res://thing.obj", "material": "wood" }
          ]
        }
        """;

    private static BlockTypeCatalog Parse(string json) => BlockTypeCatalog.Parse(Encoding.UTF8.GetBytes(json), new FakeResolver());

    private static string Block(string body) =>
        "{ \"formatVersion\": 1, \"materials\": [ { \"key\": \"stone\", \"color\": \"#808080\" } ], \"blocks\": [ " + body + " ] }";

    private static void AssertInvalid(string json, string path)
    {
        var e = Assert.Throws<InvalidDataException>(() => Parse(json));
        Assert.StartsWith(path, e.Message, StringComparison.Ordinal);
    }

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
        Assert.Equal(new BlockTypeDef("core:wood_planks", BlockShape.Cube, 3, false), wood.Def);
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

    [Fact]
    public void Parse_InvalidJson_Throws() => AssertInvalid("{ not json", "$:");

    [Theory]
    [InlineData("[]", "$")]
    [InlineData("{ \"materials\": [], \"blocks\": [] }", "$.formatVersion")]
    [InlineData("{ \"formatVersion\": 2, \"materials\": [], \"blocks\": [] }", "$.formatVersion")]
    [InlineData("{ \"formatVersion\": 1, \"blocks\": [] }", "$.materials")]
    [InlineData("{ \"formatVersion\": 1, \"materials\": [] }", "$.blocks")]
    public void Parse_BadRoot_ThrowsWithPath(string json, string path) => AssertInvalid(json, path);

    [Theory]
    [InlineData("{ \"key\": \"nocolon\", \"shape\": \"cube\", \"material\": \"stone\" }", "$.blocks[0].key")]
    [InlineData("{ \"key\": \"Core:Upper\", \"shape\": \"cube\", \"material\": \"stone\" }", "$.blocks[0].key")]
    [InlineData("{ \"key\": \"core:missing\", \"shape\": \"cube\", \"material\": \"stone\" }", "$.blocks[0].key")]
    [InlineData("{ \"shape\": \"cube\", \"material\": \"stone\" }", "$.blocks[0].key")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"sphere\", \"material\": \"stone\" }", "$.blocks[0].shape")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"cube\", \"material\": \"lava\" }", "$.blocks[0].material")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"cube\", \"material\": \"core:missing\" }", "$.blocks[0].material")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"mesh\", \"material\": \"stone\" }", "$.blocks[0].mesh")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"cube\", \"mesh\": \"builtin:slab\", \"material\": \"stone\" }", "$.blocks[0].mesh")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"mesh\", \"mesh\": \"builtin:unknown\", \"material\": \"stone\" }", "$.blocks[0].mesh")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"mesh\", \"mesh\": \"res://bad_index.obj\", \"material\": \"stone\" }", "$.blocks[0].mesh")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"mesh\", \"mesh\": \"res://bad_range.obj\", \"material\": \"stone\" }", "$.blocks[0].mesh")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"mesh\", \"mesh\": \"res://bad_count.obj\", \"material\": \"stone\" }", "$.blocks[0].mesh")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"mesh\", \"mesh\": \"res://nan.obj\", \"material\": \"stone\" }", "$.blocks[0].mesh")]
    [InlineData("{ \"key\": \"a:b\", \"shape\": \"cube\", \"material\": \"stone\", \"collision\": 1 }", "$.blocks[0].collision")]
    public void Parse_BadBlock_ThrowsWithPath(string block, string path) => AssertInvalid(Block(block), path);

    [Fact]
    public void Parse_DuplicateKey_Throws() =>
        AssertInvalid(
            Block("{ \"key\": \"a:b\", \"shape\": \"cube\", \"material\": \"stone\" }, { \"key\": \"a:b\", \"shape\": \"cube\", \"material\": \"stone\" }"),
            "$.blocks[1].key");

    [Fact]
    public void Parse_KeyLongerThanProtocolLimit_Throws() =>
        AssertInvalid(Block($"{{ \"key\": \"a:{new string('b', 70)}\", \"shape\": \"cube\", \"material\": \"stone\" }}"), "$.blocks[0].key");

    [Theory]
    [InlineData("{ \"key\": \"bad key\", \"color\": \"#000000\" }", "$.materials[0].key")]
    [InlineData("{ \"key\": \"x\", \"color\": \"red\" }", "$.materials[0].color")]
    [InlineData("{ \"key\": \"x\" }", "$.materials[0].color")]
    public void Parse_BadMaterial_ThrowsWithPath(string material, string path) =>
        AssertInvalid("{ \"formatVersion\": 1, \"materials\": [ " + material + " ], \"blocks\": [] }", path);

    [Fact]
    public void Parse_DuplicateMaterial_Throws() =>
        AssertInvalid(
            "{ \"formatVersion\": 1, \"materials\": [ { \"key\": \"x\", \"color\": \"#000000\" }, { \"key\": \"x\", \"color\": \"#000000\" } ], \"blocks\": [] }",
            "$.materials[1].key");

    [Fact]
    public void Parse_TooManyMaterials_Throws()
    {
        var materials = string.Join(",", Enumerable.Range(0, 255).Select(i => $"{{ \"key\": \"m{i}\", \"color\": \"#000000\" }}"));
        AssertInvalid("{ \"formatVersion\": 1, \"materials\": [" + materials + "], \"blocks\": [] }", "$.materials");
    }

    [Fact]
    public void Parse_TooManyBlockTypes_Throws()
    {
        var blocks = string.Join(",", Enumerable.Range(0, 2048).Select(i => $"{{ \"key\": \"t:b{i}\", \"shape\": \"cube\", \"material\": \"stone\" }}"));
        AssertInvalid(Block(blocks), "$.blocks");
    }

    [Fact]
    public void Parse_MaxBlockTypes_IsAccepted()
    {
        var blocks = string.Join(",", Enumerable.Range(0, 2047).Select(i => $"{{ \"key\": \"t:b{i}\", \"shape\": \"cube\", \"material\": \"stone\" }}"));
        Assert.Equal(2047, Parse(Block(blocks)).BlockTypeKeys.Count);
    }

    [Fact]
    public void RepositoryBlocksJson_Parses()
    {
        var path = Path.Combine(RepoRoot(), "game", "content", "blocks", "blocks.json");
        var catalog = BlockTypeCatalog.Parse(File.ReadAllBytes(path), new FakeResolver());

        Assert.Equal(
            ["core:stone_block", "core:wood_planks", "core:metal_plate", "core:stone_slab", "core:stone_slope"], catalog.BlockTypeKeys);
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
