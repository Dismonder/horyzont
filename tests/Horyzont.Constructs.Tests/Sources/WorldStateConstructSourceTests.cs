using System.Numerics;

using Horyzont.Constructs.Meshing;
using Horyzont.Constructs.Sources;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Constructs.Tests.Sources;

public sealed class WorldStateConstructSourceTests
{
    private static readonly FrameRef _planet = FrameRef.Of(WorldFactory.HomePlanet);

    private readonly ServerSimulation _sim;
    private readonly WorldStateConstructSource _source;
    private readonly BlockState _stone;

    public WorldStateConstructSourceTests()
    {
        var state = WorldFactory.CreateDefault();
        _sim = new ServerSimulation(state, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        _source = new WorldStateConstructSource(state);
        state.BlockTypes.TryGetId(WorldFactory.StoneBlockKey, out var stone);
        _stone = BlockState.Create(stone, 3);
    }

    private EntityId Create(CellPos first, Vector3? origin = null)
    {
        var id = _sim.State.EntityIds.PeekNext();
        _sim.ApplyServerOperation(new ConstructCreateOp(id, _planet, origin ?? new Vector3(0, 4000, 0), Quaternion.Identity, first, _stone));
        return id;
    }

    private void Set(EntityId id, CellPos cell) => _sim.ApplyServerOperation(new BlockSetOp(id, cell, _stone));

    [Fact]
    public void ReadsConstructAfterCreateSetAndClear()
    {
        var id = Create(new CellPos(1, 2, 3), new Vector3(10, 4000, 20));
        Set(id, new CellPos(17, 2, 3));
        Set(id, new CellPos(-1, 0, 0));

        Assert.True(_source.TryGetPose(id, out var pose));
        Assert.Equal(id, pose.Id);
        Assert.Equal(_planet, pose.Parent);
        Assert.Equal(new Vector3(10, 4000, 20), pose.Origin);
        Assert.True(pose.Version >= 2);
        Assert.Equal(new CellPos(-1, 0, 0), pose.BoundsMin);
        Assert.Equal(new CellPos(17, 2, 3), pose.BoundsMax);

        Assert.Equal(_stone, _source.GetBlock(id, new CellPos(1, 2, 3)));
        Assert.True(_source.GetBlock(id, new CellPos(2, 2, 3)).IsEmpty);
        Assert.True(_source.GetBlock(new EntityId(999), new CellPos(1, 2, 3)).IsEmpty);

        Assert.True(_source.TryGetChunkBlocks(id, new ChunkPos(1, 0, 0), out var blocks));
        Assert.Equal(_stone.Packed, blocks[new CellPos(17, 2, 3).LocalIndex]);
        Assert.False(_source.TryGetChunkBlocks(id, new ChunkPos(5, 5, 5), out _));

        _sim.ApplyServerOperation(new BlockClearOp(id, new CellPos(17, 2, 3)));

        Assert.False(_source.TryGetChunkBlocks(id, new ChunkPos(1, 0, 0), out _));
        Assert.True(_source.GetBlock(id, new CellPos(17, 2, 3)).IsEmpty);
        Assert.True(_source.TryGetPose(id, out var after));
        Assert.True(after.Version > pose.Version);
    }

    [Fact]
    public void ClearingTheLastBlock_RemovesTheConstruct()
    {
        var id = Create(new CellPos(0, 0, 0));

        _sim.ApplyServerOperation(new BlockClearOp(id, new CellPos(0, 0, 0)));

        Assert.False(_source.TryGetPose(id, out _));
        List<EntityId> constructs = [];
        _source.CollectConstructs(_planet, constructs);
        Assert.Empty(constructs);
    }

    [Fact]
    public void CollectConstructs_IsSortedByEntityId_AndFiltersByFrame()
    {
        var a = Create(new CellPos(0, 0, 0));
        var b = Create(new CellPos(0, 0, 0));
        var c = Create(new CellPos(0, 0, 0));

        List<EntityId> found = [new EntityId(1000)];
        _source.CollectConstructs(_planet, found);
        Assert.Equal([new EntityId(1000), a, b, c], found);
        Assert.True(a.Value < b.Value && b.Value < c.Value);

        found.Clear();
        _source.CollectConstructs(FrameRef.Of(new PlanetId(99)), found);
        Assert.Empty(found);
    }

    [Fact]
    public void CollectChunks_IsSorted_IncludingNegativeChunks()
    {
        var id = Create(new CellPos(40, 0, 0));
        Set(id, new CellPos(-20, 0, 0));
        Set(id, new CellPos(0, 33, 0));
        Set(id, new CellPos(0, 0, -17));
        Set(id, new CellPos(3, 3, 3));

        List<ChunkPos> chunks = [];
        _source.CollectChunks(id, chunks);

        Assert.Equal(
            [new ChunkPos(-2, 0, 0), new ChunkPos(0, 0, -2), new ChunkPos(0, 0, 0), new ChunkPos(0, 2, 0), new ChunkPos(2, 0, 0)], chunks);
        chunks.Clear();
        _source.CollectChunks(new EntityId(12345), chunks);
        Assert.Empty(chunks);
    }

    [Fact]
    public void Fill_FromTheWorld_CopiesChunkAndNeighbourSlabs()
    {
        var id = Create(new CellPos(15, 4, 4));
        Set(id, new CellPos(16, 4, 4));
        Set(id, new CellPos(14, 4, 4));

        var input = new ChunkMeshInput();
        Assert.True(input.Fill(_source, new ConstructChunkKey(id, new ChunkPos(0, 0, 0)), 5));

        Assert.Equal(5u, input.Generation);
        Assert.Equal(_stone.Packed, input.Cells[ChunkMeshInput.Index(15, 4, 4)]);
        Assert.Equal(_stone.Packed, input.Cells[ChunkMeshInput.Index(14, 4, 4)]);
        Assert.Equal(_stone.Packed, input.Cells[ChunkMeshInput.Index(16, 4, 4)]);
        Assert.Equal(3, input.Cells.ToArray().Count(c => c != 0));
    }
}
