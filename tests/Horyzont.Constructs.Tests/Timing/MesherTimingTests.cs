using System.Diagnostics;

using Horyzont.Constructs.Content;
using Horyzont.Constructs.Meshing;
using Horyzont.Constructs.Sources;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Constructs.Tests.Timing;

[CollectionDefinition("Timing", DisableParallelization = true)]
public sealed class TimingDefinition;

[Collection("Timing")]
[Trait("Category", "Slow")]
public sealed class MesherTimingTests
{
    private const string Json = """
        {
          "formatVersion": 1,
          "materials": [ { "key": "stone", "color": "#8A8D91" }, { "key": "wood", "color": "#A67B4B" }, { "key": "metal", "color": "#B8C4D0" } ],
          "blocks": [
            { "key": "core:stone_block", "shape": "cube", "material": "stone" },
            { "key": "core:wood_planks", "shape": "cube", "material": "wood" },
            { "key": "core:metal_plate", "shape": "cube", "material": "metal" },
            { "key": "core:stone_slab", "shape": "mesh", "mesh": "builtin:slab", "material": "stone" },
            { "key": "core:stone_slope", "shape": "mesh", "mesh": "builtin:slope", "material": "stone" }
          ]
        }
        """;

    [Fact]
    public void Mesh64Patterns_ReportsTimes()
    {
        var catalog = BlockTypeCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(Json), new NoMeshes());
        foreach (var pattern in Enum.GetValues<ConstructPattern>())
        {
            var state = ConstructFixtures.Create(pattern);
            var table = BlockRenderTable.Build(state.BlockTypes.Entries, catalog);
            var source = new WorldStateConstructSource(state);
            List<ChunkPos> chunks = [];
            source.CollectChunks(ConstructFixtures.ConstructId, chunks);
            var input = new ChunkMeshInput();
            var mesher = new ChunkMesher();
            using var output = new ChunkMeshData();

            // Warm-up on the first chunk so JIT and buffer growth are not measured.
            for (var i = 0; i < 3; i++)
            {
                input.Fill(source, new ConstructChunkKey(ConstructFixtures.ConstructId, chunks[0]), 1);
                mesher.Build(input, table, output);
            }

            var times = new double[chunks.Count];
            long quads = 0;
            long collisionTriangles = 0;
            var total = Stopwatch.StartNew();
            for (var i = 0; i < chunks.Count; i++)
            {
                var start = Stopwatch.GetTimestamp();
                input.Fill(source, new ConstructChunkKey(ConstructFixtures.ConstructId, chunks[i]), 1);
                mesher.Build(input, table, output);
                times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                quads += (output.Cubes.VertexCount / 4) + (output.Shapes.Indices.Length / 6);
                collisionTriangles += output.CollisionFaces.Length / 3;
            }

            total.Stop();
            Array.Sort(times);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{pattern}: chunks={chunks.Count} median={times[times.Length / 2]:F3} ms max={times[^1]:F3} ms total={total.Elapsed.TotalMilliseconds:F1} ms " +
                $"quads~{quads} collisionTriangles={collisionTriangles}");

            if (pattern == ConstructPattern.Solid)
            {
                Assert.True(total.Elapsed < TimeSpan.FromSeconds(2), $"Solid 64^3 took {total.Elapsed.TotalMilliseconds:F0} ms on one thread");
            }
        }
    }

    private sealed class NoMeshes : IShapeMeshResolver
    {
        public bool TryResolve(string meshRef, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ShapeMesh? mesh)
        {
            mesh = null;
            return false;
        }
    }
}
