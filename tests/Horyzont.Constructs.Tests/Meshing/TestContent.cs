using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;

using Horyzont.Constructs.Content;
using Horyzont.Constructs.Meshing;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Constructs.Tests.Meshing;

/// <summary>Content, render table and helpers shared by the mesher tests.</summary>
internal static class TestContent
{
    public const ushort Stone = 1;
    public const ushort Wood = 2;
    public const ushort Ghost = 3;
    public const ushort Slab = 4;
    public const ushort Slope = 5;

    public static readonly EntityId ConstructId = new(1);

    private const string Json = """
        {
          "formatVersion": 2,
          "materials": [ { "key": "stone", "color": "#808080" }, { "key": "wood", "color": "#A67B4B" } ],
          "blocks": [
            { "key": "core:stone_block", "shape": "cube", "mass": 100, "material": "stone" },
            { "key": "core:wood_planks", "shape": "cube", "mass": 100, "material": "wood" },
            { "key": "core:ghost", "shape": "cube", "mass": 100, "material": "wood", "collision": false },
            { "key": "core:stone_slab", "shape": "mesh", "mesh": "builtin:slab", "mass": 100, "material": "stone" },
            { "key": "core:stone_slope", "shape": "mesh", "mesh": "builtin:slope", "mass": 100, "material": "stone" }
          ]
        }
        """;

    public static readonly BlockTypeCatalog Catalog = BlockTypeCatalog.Parse(Encoding.UTF8.GetBytes(Json), new NoMeshes());

    public static readonly BlockRenderTable Table = BlockRenderTable.Build(
        [.. Catalog.BlockTypeKeys.Select((key, i) => new RegistryEntry((ushort)(i + 1), key)), new(6, BlockTypeRegistry.MissingKey)], Catalog);

    public static ConstructChunkKey Key(int x = 0, int y = 0, int z = 0) => new(ConstructId, new ChunkPos(x, y, z));

    public static uint Block(ushort type, byte orientation = 0) => BlockState.Create(type, orientation).Packed;

    public static ChunkMeshInput Input(params (int X, int Y, int Z, uint Value)[] cells)
    {
        var input = new ChunkMeshInput();
        input.Reset(Key(), 1);
        foreach (var (x, y, z, value) in cells)
        {
            input.Cells[ChunkMeshInput.Index(x, y, z)] = value;
        }

        return input;
    }

    public static ChunkMeshData Mesh(ChunkMeshInput input)
    {
        var output = new ChunkMeshData();
        new ChunkMesher().Build(input, Table, output);
        return output;
    }

    /// <summary>Quads of the cube surface as (face, layer cell, material) rectangles in unit faces.</summary>
    public static Dictionary<(int X, int Y, int Z, Face Face), int> RenderFaces(ChunkMeshData data)
    {
        var faces = new Dictionary<(int, int, int, Face), int>();
        var surface = data.Cubes;
        var positions = surface.Positions;
        var normals = surface.Normals;
        var layers = surface.Uv2;
        Assert.Equal(0, surface.VertexCount % 4);
        Assert.Equal(surface.VertexCount / 4 * 6, surface.Indices.Length);
        for (var v = 0; v < surface.VertexCount; v += 4)
        {
            var face = FaceOf(normals[v]);
            for (var k = 1; k < 4; k++)
            {
                Assert.Equal(normals[v], normals[v + k]);
                Assert.Equal(layers[v].X, layers[v + k].X);
            }

            // Triangles (0,2,1) and (0,3,2) are clockwise seen from outside.
            AssertClockwise(positions[v], positions[v + 2], positions[v + 1], normals[v]);
            AssertClockwise(positions[v], positions[v + 3], positions[v + 2], normals[v]);
            var material = (int)layers[v].X + 1;
            foreach (var cell in Cells(face, positions.Slice(v, 4)))
            {
                Assert.True(faces.TryAdd((cell.X, cell.Y, cell.Z, face), material), $"face {face} of cell {cell} is covered twice");
            }
        }

        return faces;
    }

    /// <summary>Collision triangles as unit faces; every unit face must be covered by exactly two triangles.</summary>
    public static HashSet<(int X, int Y, int Z, Face Face)> CollisionFaces(ChunkMeshData data)
    {
        var counts = new Dictionary<(int, int, int, Face), int>();
        var faces = data.CollisionFaces;
        Assert.Equal(0, faces.Length % 3);
        for (var t = 0; t < faces.Length; t += 3)
        {
            var clockwiseNormal = -Vector3.Cross(faces[t + 1] - faces[t], faces[t + 2] - faces[t]);
            var face = FaceOf(Vector3.Normalize(clockwiseNormal));
            foreach (var cell in Cells(face, faces.Slice(t, 3)))
            {
                counts[(cell.X, cell.Y, cell.Z, face)] = counts.GetValueOrDefault((cell.X, cell.Y, cell.Z, face)) + 1;
            }
        }

        Assert.All(counts, pair => Assert.Equal(2, pair.Value));
        return [.. counts.Keys];
    }

    public static Face FaceOf(Vector3 normal)
    {
        if (normal.X > 0.99f)
        {
            return Face.PosX;
        }

        if (normal.X < -0.99f)
        {
            return Face.NegX;
        }

        if (normal.Y > 0.99f)
        {
            return Face.PosY;
        }

        if (normal.Y < -0.99f)
        {
            return Face.NegY;
        }

        Assert.True(MathF.Abs(normal.Z) > 0.99f, $"normal {normal} is not axis aligned");
        return normal.Z > 0 ? Face.PosZ : Face.NegZ;
    }

    private static void AssertClockwise(Vector3 a, Vector3 b, Vector3 c, Vector3 outward) =>
        Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0, "triangle must be clockwise seen from outside");

    private static List<(int X, int Y, int Z)> Cells(Face face, ReadOnlySpan<Vector3> corners)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in corners)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        var lo = new[] { (int)min.X, (int)min.Y, (int)min.Z };
        var hi = new[] { (int)max.X, (int)max.Y, (int)max.Z };
        var axis = (int)face >> 1;
        var layer = ((int)face & 1) == 0 ? lo[axis] - 1 : lo[axis];
        Assert.Equal(lo[axis], hi[axis]);
        lo[axis] = layer;
        hi[axis] = layer + 1;
        var result = new List<(int X, int Y, int Z)>();
        for (var x = lo[0]; x < hi[0]; x++)
        {
            for (var y = lo[1]; y < hi[1]; y++)
            {
                for (var z = lo[2]; z < hi[2]; z++)
                {
                    result.Add((x, y, z));
                }
            }
        }

        return result;
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
