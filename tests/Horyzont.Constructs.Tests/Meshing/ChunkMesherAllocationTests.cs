using Horyzont.Constructs.Meshing;
using Horyzont.Core.World.Constructs;

using static Horyzont.Constructs.Tests.Meshing.TestContent;

namespace Horyzont.Constructs.Tests.Meshing;

public sealed class ChunkMesherAllocationTests
{
    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        // A random chunk with mesh shapes, and neighbours on all six sides so Fill copies every slab.
        var source = new ArrayConstructSource();
        var centre = ChunkMesherTests.RandomInput(7, 35);
        var blocks = new uint[ConstructChunk.CellCount];
        for (var x = 0; x < 16; x++)
        {
            for (var y = 0; y < 16; y++)
            {
                for (var z = 0; z < 16; z++)
                {
                    blocks[(x << 8) | (y << 4) | z] = centre.Cells[ChunkMeshInput.Index(x, y, z)];
                }
            }
        }

        source.SetChunk(new ChunkPos(0, 0, 0), blocks);
        foreach (var neighbour in new[] { new ChunkPos(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) })
        {
            source.SetChunk(neighbour, (uint[])blocks.Clone());
        }

        var input = new ChunkMeshInput();
        var mesher = new ChunkMesher();
        using var output = new ChunkMeshData();

        // Warm-up: JIT, pool arrays and output buffers reach their high-water mark.
        for (var i = 0; i < 3; i++)
        {
            Assert.True(input.Fill(source, Key(), 1));
            mesher.Build(input, Table, output);
        }

        Assert.False(output.IsEmpty);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20; i++)
        {
            input.Fill(source, Key(), 1);
            mesher.Build(input, Table, output);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }
}
