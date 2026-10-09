using FsCheck.Xunit;

using Horyzont.Constructs.Meshing;

using static Horyzont.Constructs.Tests.Meshing.TestContent;

namespace Horyzont.Constructs.Tests.Meshing;

public sealed class ChunkMesherPropertyTests
{
    [Property(MaxTest = 120)]
    public void GreedyCoversExactlyExposedFaces(ulong seed, byte density)
    {
        var input = ChunkMesherTests.RandomInput(seed, density % 101, shapes: false);
        var (expectedRender, expectedCollision) = NaiveFaceOracle.Faces(input.Cells);

        var data = Mesh(input);
        var render = RenderFaces(data); // also fails on overlapping quads and wrong winding
        var collision = CollisionFaces(data);

        Assert.Equal(expectedRender.Count, render.Count);
        foreach (var (face, material) in expectedRender)
        {
            Assert.True(render.TryGetValue(face, out var actual), $"missing render face {face}");
            Assert.Equal(material, actual);
        }

        Assert.True(expectedCollision.SetEquals(collision));
    }

    [Property(MaxTest = 120)]
    public void WithShapes_CubeFacesMatchOracle_AndShapesAreCounted(ulong seed, byte density)
    {
        var input = ChunkMesherTests.RandomInput(seed, density % 101);
        var (expectedRender, _) = NaiveFaceOracle.Faces(input.Cells);
        var shapeVertices = 0;
        var shapeIndices = 0;
        for (var x = 0; x < 16; x++)
        {
            for (var y = 0; y < 16; y++)
            {
                for (var z = 0; z < 16; z++)
                {
                    var type = new Horyzont.Core.World.Constructs.BlockState(input.Cells[ChunkMeshInput.Index(x, y, z)]).TypeId;
                    if (type == Slab)
                    {
                        shapeVertices += 24;
                        shapeIndices += 36;
                    }
                    else if (type == Slope)
                    {
                        shapeVertices += 18;
                        shapeIndices += 24;
                    }
                }
            }
        }

        var data = Mesh(input);

        Assert.Equal(expectedRender.Count, RenderFaces(data).Count);
        Assert.Equal(shapeVertices, data.Shapes.VertexCount);
        Assert.Equal(shapeIndices, data.Shapes.Indices.Length);
        Assert.Equal(0, data.CollisionFaces.Length % 3);
    }

    [Property(MaxTest = 60)]
    public void ArbitraryUints_NeverThrow(ulong seed, byte density)
    {
        var input = new ChunkMeshInput();
        input.Reset(Key(), 1);
        var state = seed;
        for (var i = 0; i < input.Cells.Length; i++)
        {
            state += 0x9E3779B97F4A7C15UL;
            var r = state;
            r = (r ^ (r >> 30)) * 0xBF58476D1CE4E5B9UL;
            r = (r ^ (r >> 27)) * 0x94D049BB133111EBUL;
            r ^= r >> 31;
            if ((int)(r % 100) < density % 101)
            {
                input.Cells[i] = (uint)(r >> 20);
            }
        }

        var data = new ChunkMeshData();
        new ChunkMesher().Build(input, Table, data);

        // Triangle structure stays consistent whatever the input.
        Assert.Equal(0, data.Cubes.VertexCount % 4);
        Assert.Equal(0, data.CollisionFaces.Length % 3);
        foreach (var index in data.Cubes.Indices)
        {
            Assert.InRange(index, 0, data.Cubes.VertexCount - 1);
        }

        foreach (var index in data.Shapes.Indices)
        {
            Assert.InRange(index, 0, data.Shapes.VertexCount - 1);
        }

        var (expectedRender, _) = NaiveFaceOracle.Faces(input.Cells);
        Assert.Equal(expectedRender.Count, RenderFaces(data).Count);
    }
}
