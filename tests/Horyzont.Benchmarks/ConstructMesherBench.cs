using System.Text;

using BenchmarkDotNet.Attributes;

using Horyzont.Constructs.Content;
using Horyzont.Constructs.Meshing;
using Horyzont.Constructs.Sources;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Benchmarks;

/// <summary>Meshing of one 16³ chunk of a 64³ construct (as <c>MesherTimingTests</c>) for three patterns.</summary>
[MemoryDiagnoser]
public class ConstructMesherBench : IDisposable
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

    private readonly ChunkMeshInput _input = new();
    private readonly ChunkMesher _mesher = new();
    private readonly ChunkMeshData _output = new();
    private WorldStateConstructSource _source = null!;
    private BlockRenderTable _table = null!;
    private ConstructChunkKey _key;

    [Params(ConstructPattern.Solid, ConstructPattern.Random35, ConstructPattern.Checkerboard)]
    public ConstructPattern Pattern { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var catalog = BlockTypeCatalog.Parse(Encoding.UTF8.GetBytes(Json), new NoMeshes());
        var state = ConstructFixtures.Create(Pattern);
        _table = BlockRenderTable.Build(state.BlockTypes.Entries, catalog);
        _source = new WorldStateConstructSource(state);
        List<ChunkPos> chunks = [];
        _source.CollectChunks(ConstructFixtures.ConstructId, chunks);
        _key = new ConstructChunkKey(ConstructFixtures.ConstructId, chunks[chunks.Count / 2]);
    }

    [GlobalCleanup]
    public void Cleanup() => _output.Dispose();

    public void Dispose()
    {
        _output.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public void MeshChunk()
    {
        _input.Fill(_source, _key, 1);
        _mesher.Build(_input, _table, _output);
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
