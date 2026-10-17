using System.Numerics;

using Horyzont.Constructs.Meshing;
using Horyzont.Core.World.Constructs;

using static Horyzont.Constructs.Tests.Meshing.TestContent;

namespace Horyzont.Constructs.Tests.Meshing;

public sealed class ChunkMesherTests
{
    private static int Quads(ChunkMeshData data) => data.Cubes.VertexCount / 4;

    [Fact]
    public void SingleCube_SixQuads()
    {
        var data = Mesh(Input((5, 5, 5, Block(Stone))));

        Assert.Equal(6, Quads(data));
        Assert.Equal(36, data.Cubes.Indices.Length);
        Assert.Equal(36, data.CollisionFaces.Length);
        Assert.True(data.Shapes.IsEmpty);
        Assert.Equal(6, RenderFaces(data).Count);
        Assert.Equal(6, CollisionFaces(data).Count);
        Assert.All(data.Cubes.Uv2.ToArray(), uv => Assert.Equal(1f, uv.X)); // stone is material 2 -> layer 1
    }

    [Fact]
    public void SolidChunk_SixQuads()
    {
        var input = new ChunkMeshInput();
        input.Reset(Key(), 1);
        for (var x = 0; x < 16; x++)
        {
            for (var y = 0; y < 16; y++)
            {
                for (var z = 0; z < 16; z++)
                {
                    input.Cells[ChunkMeshInput.Index(x, y, z)] = Block(Stone);
                }
            }
        }

        var data = Mesh(input);

        Assert.Equal(6, Quads(data));
        Assert.Equal(6 * 6, data.CollisionFaces.Length);
        Assert.Equal(6 * 256, RenderFaces(data).Count);
        Assert.Equal(6 * 256, CollisionFaces(data).Count);
    }

    [Fact]
    public void TwoCubes_CullSharedFace()
    {
        var data = Mesh(Input((3, 3, 3, Block(Stone)), (4, 3, 3, Block(Stone))));

        // Four merged 2x1 side quads and two end caps; the shared face is hidden.
        Assert.Equal(6, Quads(data));
        var faces = RenderFaces(data);
        Assert.Equal(10, faces.Count);
        Assert.DoesNotContain((3, 3, 3, Face.PosX), faces.Keys);
        Assert.DoesNotContain((4, 3, 3, Face.NegX), faces.Keys);
    }

    [Fact]
    public void TwoCubes_DifferentMaterials_DoNotMerge()
    {
        var data = Mesh(Input((3, 3, 3, Block(Stone)), (4, 3, 3, Block(Wood))));

        Assert.Equal(10, Quads(data));
        var faces = RenderFaces(data);
        Assert.Equal(2, faces[(3, 3, 3, Face.PosY)]);
        Assert.Equal(3, faces[(4, 3, 3, Face.PosY)]);
    }

    [Fact]
    public void Border_HidesFacesTowardsNeighbourChunk_AndKeepsThemWhenEmpty()
    {
        var withNeighbour = Mesh(Input((15, 4, 4, Block(Stone)), (16, 4, 4, Block(Stone))));
        var alone = Mesh(Input((15, 4, 4, Block(Stone))));

        Assert.DoesNotContain((15, 4, 4, Face.PosX), RenderFaces(withNeighbour).Keys);
        Assert.DoesNotContain((15, 4, 4, Face.PosX), CollisionFaces(withNeighbour));
        Assert.Contains((15, 4, 4, Face.PosX), RenderFaces(alone).Keys);
    }

    [Fact]
    public void Solid64Construct_96RenderQuads()
    {
        var source = new ArrayConstructSource();
        for (var cx = 0; cx < 4; cx++)
        {
            for (var cy = 0; cy < 4; cy++)
            {
                for (var cz = 0; cz < 4; cz++)
                {
                    source.SetChunk(new ChunkPos(cx, cy, cz), Enumerable.Repeat(Block(Stone), ConstructChunk.CellCount).ToArray());
                }
            }
        }

        var input = new ChunkMeshInput();
        var mesher = new ChunkMesher();
        using var output = new ChunkMeshData();
        var quads = 0;
        var collisionTriangles = 0;
        for (var cx = 0; cx < 4; cx++)
        {
            for (var cy = 0; cy < 4; cy++)
            {
                for (var cz = 0; cz < 4; cz++)
                {
                    Assert.True(input.Fill(source, Key(cx, cy, cz), 1));
                    mesher.Build(input, Table, output);
                    quads += output.Cubes.VertexCount / 4;
                    collisionTriangles += output.CollisionFaces.Length / 3;
                }
            }
        }

        Assert.Equal(6 * 16, quads);
        Assert.Equal(6 * 16 * 2, collisionTriangles);
    }

    [Fact]
    public void Fill_ReturnsFalse_ForMissingChunk()
    {
        var input = new ChunkMeshInput();

        Assert.False(input.Fill(new ArrayConstructSource(), Key(), 3));
        Assert.Equal(3u, input.Generation);
    }

    [Fact]
    public void Fill_CopiesOnlyFaceSlabsOfNeighbours()
    {
        var source = new ArrayConstructSource();
        source.Set(new CellPos(2, 2, 2), Block(Stone));
        source.Set(new CellPos(16, 2, 2), Block(Wood)); // +X neighbour, local x = 0
        source.Set(new CellPos(-1, 2, 2), Block(Wood)); // -X neighbour, local x = 15
        source.Set(new CellPos(2, 16, 2), Block(Wood));
        source.Set(new CellPos(2, -1, 2), Block(Wood));
        source.Set(new CellPos(2, 2, 16), Block(Wood));
        source.Set(new CellPos(2, 2, -1), Block(Wood));
        source.Set(new CellPos(16, 16, 2), Block(Wood)); // edge neighbour: must not be copied
        source.Set(new CellPos(17, 2, 2), Block(Wood)); // second cell of the neighbour: not in the slab

        var input = new ChunkMeshInput();
        Assert.True(input.Fill(source, Key(), 1));

        Assert.Equal(Block(Stone), input.Cells[ChunkMeshInput.Index(2, 2, 2)]);
        foreach (var (x, y, z) in new[] { (16, 2, 2), (-1, 2, 2), (2, 16, 2), (2, -1, 2), (2, 2, 16), (2, 2, -1) })
        {
            Assert.Equal(Block(Wood), input.Cells[ChunkMeshInput.Index(x, y, z)]);
        }

        Assert.Equal(0u, input.Cells[ChunkMeshInput.Index(16, 16, 2)]);
        Assert.Equal(7, input.Cells.ToArray().Count(c => c != 0));
    }

    [Fact]
    public void MeshShape_EmitsOrientedPrefab_DoesNotOcclude()
    {
        for (var o = 0; o < 24; o++)
        {
            var data = Mesh(Input((5, 6, 7, Block(Slab, (byte)o)), (6, 6, 7, Block(Stone))));
            var q = BlockOrientation.ToQuaternion((byte)o);

            Assert.True(BuiltInShapes.TryGetSlab(out var slab));
            Assert.Equal(slab.Positions.Length, data.Shapes.VertexCount);
            for (var i = 0; i < slab.Positions.Length; i++)
            {
                var expected = Vector3.Transform(slab.Positions[i] - new Vector3(0.5f), q) + new Vector3(5.5f, 6.5f, 7.5f);
                Assert.True((data.Shapes.Positions[i] - expected).Length() < 1e-5f, $"orientation {o} vertex {i}");
                var expectedNormal = Vector3.Transform(slab.Normals[i], q);
                Assert.True((data.Shapes.Normals[i] - expectedNormal).Length() < 1e-5f, $"orientation {o} normal {i}");
            }

            // The neighbouring cube keeps all six faces, including the one towards the slab, and the slab is not rendered as cube.
            var faces = RenderFaces(data);
            Assert.Equal(6, faces.Count);
            Assert.Contains((6, 6, 7, Face.NegX), faces.Keys);
            Assert.Equal(slab.Indices.Length, data.Shapes.Indices.Length);

            // Collision: the slab triangles plus the six cube faces.
            Assert.Equal(slab.Indices.Length + 36, data.CollisionFaces.Length);
        }
    }

    [Fact]
    public void MeshShape_Slab_StaysInsideItsCell_ForAllOrientations()
    {
        for (var o = 0; o < 24; o++)
        {
            var data = Mesh(Input((2, 2, 2, Block(Slab, (byte)o))));
            foreach (var p in data.Shapes.Positions)
            {
                Assert.InRange(p.X, 2f - 1e-5f, 3f + 1e-5f);
                Assert.InRange(p.Y, 2f - 1e-5f, 3f + 1e-5f);
                Assert.InRange(p.Z, 2f - 1e-5f, 3f + 1e-5f);
            }
        }
    }

    [Fact]
    public void NoCollisionType_RenderedButNotInCollision()
    {
        var alone = Mesh(Input((4, 4, 4, Block(Ghost))));
        Assert.Equal(6, Quads(alone));
        Assert.Empty(alone.CollisionFaces.ToArray());

        // A solid cube next to a ghost cube keeps its collision face; the ghost's render face towards the solid one is hidden.
        var pair = Mesh(Input((4, 4, 4, Block(Ghost)), (5, 4, 4, Block(Stone))));
        Assert.Contains((5, 4, 4, Face.NegX), CollisionFaces(pair));
        Assert.Equal(6, CollisionFaces(pair).Count);
        Assert.DoesNotContain((4, 4, 4, Face.PosX), RenderFaces(pair).Keys);
        Assert.DoesNotContain((5, 4, 4, Face.NegX), RenderFaces(pair).Keys);
    }

    [Fact]
    public void MalformedBlock_IsDrawnAsMissingCube()
    {
        // Orientation 31 and a known type id: not well-formed.
        var malformed = (uint)Stone | (31u << 16);
        var data = Mesh(Input((1, 1, 1, malformed)));

        Assert.Equal(6, Quads(data));
        Assert.All(data.Cubes.Uv2.ToArray(), uv => Assert.Equal(0f, uv.X)); // core:missing is material 1 -> layer 0
        Assert.Equal(36, data.CollisionFaces.Length);
    }

    [Fact]
    public void UnknownTypeId_IsDrawnAsMissingCube()
    {
        var data = Mesh(Input((1, 1, 1, BlockStateOf(1234))));

        Assert.Equal(6, Quads(data));
        Assert.All(data.Cubes.Uv2.ToArray(), uv => Assert.Equal(0f, uv.X));
    }

    [Fact]
    public void EmptyInput_ProducesEmptyOutput()
    {
        var data = Mesh(Input());

        Assert.True(data.IsEmpty);
    }

    [Fact]
    public void SameInput_IdenticalOutput()
    {
        var input = RandomInput(99, 40);
        var a = Mesh(input);
        var b = new ChunkMeshData();
        var mesher = new ChunkMesher();
        mesher.Build(input, Table, b);
        mesher.Build(input, Table, b); // reuse of a warm mesher and buffers

        Assert.True(a.Cubes.Positions.SequenceEqual(b.Cubes.Positions));
        Assert.True(a.Cubes.Normals.SequenceEqual(b.Cubes.Normals));
        Assert.True(a.Cubes.Uvs.SequenceEqual(b.Cubes.Uvs));
        Assert.True(a.Cubes.Uv2.SequenceEqual(b.Cubes.Uv2));
        Assert.True(a.Cubes.Indices.SequenceEqual(b.Cubes.Indices));
        Assert.True(a.Shapes.Positions.SequenceEqual(b.Shapes.Positions));
        Assert.True(a.Shapes.Indices.SequenceEqual(b.Shapes.Indices));
        Assert.True(a.CollisionFaces.SequenceEqual(b.CollisionFaces));
    }

    internal static uint BlockStateOf(ushort typeId) => typeId;

    internal static ChunkMeshInput RandomInput(ulong seed, int densityPercent, bool shapes = true)
    {
        var input = new ChunkMeshInput();
        input.Reset(Key(), 1);
        var state = seed;
        for (var x = -1; x <= 16; x++)
        {
            for (var y = -1; y <= 16; y++)
            {
                for (var z = -1; z <= 16; z++)
                {
                    state += 0x9E3779B97F4A7C15UL;
                    var r = state;
                    r = (r ^ (r >> 30)) * 0xBF58476D1CE4E5B9UL;
                    r = (r ^ (r >> 27)) * 0x94D049BB133111EBUL;
                    r ^= r >> 31;
                    if ((int)(r % 100) >= densityPercent)
                    {
                        continue;
                    }

                    var type = (ushort)(1 + (int)((r >> 8) % 6));
                    if (!shapes && type is Slab or Slope)
                    {
                        type = Stone;
                    }

                    input.Cells[ChunkMeshInput.Index(x, y, z)] = Block(type, (byte)((r >> 16) % 24));
                }
            }
        }

        return input;
    }

    [Fact]
    public void OpenDoor_NoCollision_DrawsOpenPanel()
    {
        var table = FunctionalMeshContent.Door();

        var data = FunctionalMeshContent.Mesh(table, Input((5, 5, 5, FunctionalMeshContent.PackedFunctional(1))));

        // An open door lets the player through: nothing to collide with, no full cube, a thin panel as a prefab shape inside the cell.
        Assert.Equal(0, data.CollisionFaces.Length);
        Assert.True(data.Cubes.IsEmpty);
        Assert.False(data.Shapes.IsEmpty);
        foreach (var p in data.Shapes.Positions)
        {
            Assert.InRange(p.X, 5f, 6f);
            Assert.InRange(p.Y, 5f, 6f);
            Assert.InRange(p.Z, 5f, 6f);
        }
    }

    [Fact]
    public void ClosedDoor_Collides()
    {
        var table = FunctionalMeshContent.Door();

        var closed = FunctionalMeshContent.Mesh(table, Input((5, 5, 5, FunctionalMeshContent.PackedFunctional(0))));
        var open = FunctionalMeshContent.Mesh(table, Input((5, 5, 5, FunctionalMeshContent.PackedFunctional(1))));

        // A closed door is a cube like any other; the same cell opened is not (the contrast keeps this test from passing on a mesher that ignores the state).
        Assert.Equal(36, closed.CollisionFaces.Length);
        Assert.Equal(6, Quads(closed));
        Assert.True(closed.Shapes.IsEmpty);
        Assert.Equal(0, open.CollisionFaces.Length);
    }

    [Fact]
    public void LitLamp_ListedInLightCells()
    {
        var table = FunctionalMeshContent.Lamp();

        var data = FunctionalMeshContent.Mesh(table, Input((5, 5, 5, FunctionalMeshContent.PackedFunctional(1)), (6, 5, 5, FunctionalMeshContent.PackedFunctional(0))));

        Assert.Equal([new CellPos(5, 5, 5)], data.LightCells.ToArray());
        Assert.False(data.Cubes.IsEmpty);
        Assert.True(data.CollisionFaces.Length > 0);
    }

    [Fact]
    public void UnlitLamp_NotListed()
    {
        var table = FunctionalMeshContent.Lamp();

        var data = FunctionalMeshContent.Mesh(table, Input((5, 5, 5, FunctionalMeshContent.PackedFunctional(0)), (9, 9, 9, FunctionalMeshContent.PackedFunctional(1))));

        // Only the lit lamp is listed; the unlit one alone lists nothing.
        Assert.Equal([new CellPos(9, 9, 9)], data.LightCells.ToArray());
        Assert.Empty(FunctionalMeshContent.Mesh(table, Input((5, 5, 5, FunctionalMeshContent.PackedFunctional(0)))).LightCells.ToArray());
    }
}

internal static class BuiltInShapes
{
    public static bool TryGetSlab(out Horyzont.Constructs.Content.ShapeMesh mesh) =>
        Horyzont.Constructs.Content.BuiltInShapeMeshes.TryResolve(Horyzont.Constructs.Content.BuiltInShapeMeshes.SlabRef, out mesh!);
}
