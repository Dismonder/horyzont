using System.Diagnostics.CodeAnalysis;
using System.Text;

using Horyzont.Constructs.Content;
using Horyzont.Constructs.Meshing;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Constructs.Tests.Meshing;

public sealed class BlockRenderTableTests
{
    private const string Json = """
        {
          "formatVersion": 1,
          "materials": [ { "key": "stone", "color": "#808080" }, { "key": "wood", "color": "#A67B4B" } ],
          "blocks": [
            { "key": "core:stone_block", "shape": "cube", "material": "stone" },
            { "key": "core:wood_planks", "shape": "cube", "material": "wood", "collision": false },
            { "key": "core:stone_slab", "shape": "mesh", "mesh": "builtin:slab", "material": "stone" }
          ]
        }
        """;

    private static readonly BlockTypeCatalog _catalog = BlockTypeCatalog.Parse(Encoding.UTF8.GetBytes(Json), new NoMeshes());

    [Fact]
    public void Get_Zero_IsEmpty()
    {
        var table = BlockRenderTable.Build([new RegistryEntry(1, "core:stone_block")], _catalog);

        Assert.True(table.Get(0).IsEmpty);
        Assert.Equal((BlockShape)0, table.Get(0).Shape);
        Assert.False(table.Get(0).Occludes);
    }

    [Fact]
    public void Get_KnownKeys_MapToContent()
    {
        var table = BlockRenderTable.Build(
            [new(1, "core:stone_block"), new(2, "core:wood_planks"), new(3, "core:stone_slab"), new(4, BlockTypeRegistry.MissingKey)], _catalog);

        var stone = table.Get(1);
        Assert.Equal(BlockShape.Cube, stone.Shape);
        Assert.Equal(2, stone.MaterialId);
        Assert.True(stone.HasCollision);
        Assert.True(stone.Occludes);
        Assert.False(stone.IsMissing);

        var wood = table.Get(2);
        Assert.Equal(3, wood.MaterialId);
        Assert.False(wood.HasCollision);

        var slab = table.Get(3);
        Assert.Equal(BlockShape.Mesh, slab.Shape);
        Assert.False(slab.Occludes);
        Assert.NotNull(slab.Mesh);

        Assert.True(table.Get(4).IsMissing);
        Assert.Equal(0, table.MissingKeyCount);
    }

    [Fact]
    public void Get_KeyUnknownToContent_IsMissingCubeWithCollision()
    {
        var table = BlockRenderTable.Build([new(1, "core:stone_block"), new(2, "mod:gone"), new(3, "mod:also_gone")], _catalog);

        var info = table.Get(2);
        Assert.True(info.IsMissing);
        Assert.Equal(BlockShape.Cube, info.Shape);
        Assert.True(info.HasCollision);
        Assert.True(info.Occludes);
        Assert.Equal(BlockTypeCatalog.MissingMaterialId, info.MaterialId);
        Assert.Equal(2, table.MissingKeyCount);
        Assert.Equal(["mod:gone", "mod:also_gone"], table.UnknownKeys);
    }

    [Fact]
    public void Get_IdOutsideRegistry_IsMissing_WithoutThrowing()
    {
        var table = BlockRenderTable.Build([new RegistryEntry(1, "core:stone_block")], _catalog);

        Assert.True(table.Get(2).IsMissing);
        Assert.True(table.Get(ushort.MaxValue).IsMissing);
        Assert.Equal(0, table.MissingKeyCount);
    }

    private sealed class NoMeshes : IShapeMeshResolver
    {
        public bool TryResolve(string meshRef, [NotNullWhen(true)] out ShapeMesh? mesh)
        {
            mesh = null;
            return false;
        }
    }
}
