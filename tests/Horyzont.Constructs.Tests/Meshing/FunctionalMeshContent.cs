using System.Diagnostics.CodeAnalysis;
using System.Text;

using Horyzont.Constructs.Content;
using Horyzont.Constructs.Meshing;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Constructs.Tests.Meshing;

/// <summary>
/// Content and render tables for the door and lamp tests of the mesher (K6-30 D6, D7). Built per call, so a content error shows in the test
/// that needs the content: the lamp catalog parses only once the <c>light</c> function exists (step B3).
/// </summary>
internal static class FunctionalMeshContent
{
    /// <summary>Type id of the stone block in both catalogs.</summary>
    public const ushort Stone = 1;

    /// <summary>Type id of the door (<see cref="Door"/>) or of the lamp (<see cref="Lamp"/>); the other catalog does not have it.</summary>
    public const ushort Functional = 2;

    private const string DoorJson = """
        {
          "formatVersion": 2,
          "materials": [ { "key": "stone", "color": "#808080" }, { "key": "wood", "color": "#A67B4B" } ],
          "blocks": [
            { "key": "core:stone_block", "shape": "cube", "mass": 100, "material": "stone" },
            { "key": "core:wood_door", "shape": "cube", "mass": 100, "material": "wood", "function": { "kind": "door" } }
          ]
        }
        """;

    private const string LampJson = """
        {
          "formatVersion": 2,
          "materials": [ { "key": "stone", "color": "#808080" }, { "key": "lamp", "color": "#F2E3B3" } ],
          "blocks": [
            { "key": "core:stone_block", "shape": "cube", "mass": 100, "material": "stone" },
            { "key": "core:lamp", "shape": "cube", "mass": 20, "material": "lamp", "function": { "kind": "light", "range": 8, "color": "#FFD9A0", "energy": 1.5 } }
          ]
        }
        """;

    /// <summary>A render table with the stone block and a wooden door (type 2).</summary>
    public static BlockRenderTable Door() => Table(DoorJson);

    /// <summary>A render table with the stone block and a lamp (type 2).</summary>
    public static BlockRenderTable Lamp() => Table(LampJson);

    /// <summary>The packed block of the functional type with a function state.</summary>
    /// <param name="state">Function state, 0 closed or off, 1 open or on.</param>
    /// <returns>The packed value for <see cref="ChunkMeshInput.Cells"/>.</returns>
    public static uint PackedFunctional(byte state) => BlockState.Create(Functional, 0, 0, state).Packed;

    /// <summary>Meshes an input with a table.</summary>
    public static ChunkMeshData Mesh(BlockRenderTable table, ChunkMeshInput input)
    {
        var output = new ChunkMeshData();
        new ChunkMesher().Build(input, table, output);
        return output;
    }

    private static BlockRenderTable Table(string json)
    {
        var catalog = BlockTypeCatalog.Parse(Encoding.UTF8.GetBytes(json), new NoMeshes());
        return BlockRenderTable.Build(
            [.. catalog.BlockTypeKeys.Select((key, i) => new RegistryEntry((ushort)(i + 1), key)), new((ushort)(catalog.BlockTypeKeys.Count + 1), BlockTypeRegistry.MissingKey)],
            catalog);
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
