using System.Numerics;

using Horyzont.Constructs.Sources;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Constructs.Tests.Sources;

public sealed class ConstructDirtyChunksTests
{
    private static readonly EntityId _id = new(7);
    private static readonly BlockState _stone = BlockState.Create(1, 0);

    private static (List<ConstructChunkKey> Dirty, List<EntityId> Created, List<EntityId> Removed, List<EntityId> Reposed) Run(
        Operation operation, params ConstructChange[] changes)
    {
        List<ConstructChunkKey> dirty = [];
        List<EntityId> created = [];
        List<EntityId> removed = [];
        List<EntityId> reposed = [];
        ConstructDirtyChunks.Collect(operation, changes, dirty, created, removed, reposed);
        return (dirty, created, removed, reposed);
    }

    private static ChunkPos[] Chunks(List<ConstructChunkKey> dirty)
    {
        Assert.All(dirty, key => Assert.Equal(_id, key.Construct));
        return [.. dirty.Select(k => k.Chunk).Order()];
    }

    [Fact]
    public void BlockSet_InsideChunk_DirtiesOneChunk()
    {
        var (dirty, created, removed, _) = Run(new BlockSetOp(_id, new CellPos(20, 8, 40), _stone), new ConstructChange(_id, 5, false));

        Assert.Equal([new ChunkPos(1, 0, 2)], Chunks(dirty));
        Assert.Empty(created);
        Assert.Empty(removed);
    }

    [Fact]
    public void BlockSet_OnChunkFace_DirtiesTwoChunks()
    {
        Assert.Equal([new ChunkPos(-1, 0, 0), new ChunkPos(0, 0, 0)], Chunks(Run(new BlockSetOp(_id, new CellPos(0, 8, 8), _stone)).Dirty));
        Assert.Equal([new ChunkPos(0, 0, 0), new ChunkPos(1, 0, 0)], Chunks(Run(new BlockSetOp(_id, new CellPos(15, 8, 8), _stone)).Dirty));
        Assert.Equal([new ChunkPos(0, 0, 0), new ChunkPos(0, 1, 0)], Chunks(Run(new BlockSetOp(_id, new CellPos(8, 15, 8), _stone)).Dirty));
        Assert.Equal([new ChunkPos(0, 0, -1), new ChunkPos(0, 0, 0)], Chunks(Run(new BlockSetOp(_id, new CellPos(8, 8, 0), _stone)).Dirty));
    }

    [Fact]
    public void BlockSet_OnEdge_DirtiesThreeChunks()
    {
        var dirty = Run(new BlockSetOp(_id, new CellPos(0, 15, 8), _stone)).Dirty;

        Assert.Equal(3, dirty.Count);
        Assert.Contains(new ConstructChunkKey(_id, new ChunkPos(0, 0, 0)), dirty);
        Assert.Contains(new ConstructChunkKey(_id, new ChunkPos(-1, 0, 0)), dirty);
        Assert.Contains(new ConstructChunkKey(_id, new ChunkPos(0, 1, 0)), dirty);
    }

    [Fact]
    public void BlockSet_OnCorner_DirtiesFourChunks()
    {
        var dirty = Run(new BlockSetOp(_id, new CellPos(15, 15, 15), _stone)).Dirty;

        Assert.Equal(4, dirty.Count);
        Assert.Equal(4, dirty.Distinct().Count());
        Assert.Contains(new ConstructChunkKey(_id, new ChunkPos(0, 0, 0)), dirty);
        Assert.Contains(new ConstructChunkKey(_id, new ChunkPos(1, 0, 0)), dirty);
        Assert.Contains(new ConstructChunkKey(_id, new ChunkPos(0, 1, 0)), dirty);
        Assert.Contains(new ConstructChunkKey(_id, new ChunkPos(0, 0, 1)), dirty);
    }

    [Fact]
    public void BlockSet_NegativeCoordinates_UseFloorChunks()
    {
        // (-1,-1,-1) is local (15,15,15) of chunk (-1,-1,-1); its face neighbours are one chunk up on each axis.
        var dirty = Run(new BlockSetOp(_id, new CellPos(-1, -1, -1), _stone)).Dirty;

        Assert.Equal(4, dirty.Count);
        Assert.Contains(new ConstructChunkKey(_id, new ChunkPos(-1, -1, -1)), dirty);
        Assert.Contains(new ConstructChunkKey(_id, new ChunkPos(0, -1, -1)), dirty);
        Assert.Contains(new ConstructChunkKey(_id, new ChunkPos(-1, 0, -1)), dirty);
        Assert.Contains(new ConstructChunkKey(_id, new ChunkPos(-1, -1, 0)), dirty);
    }

    [Fact]
    public void BlockClear_DirtiesTheSameChunksAsBlockSet()
    {
        var set = Run(new BlockSetOp(_id, new CellPos(0, 3, 15), _stone)).Dirty;
        var clear = Run(new BlockClearOp(_id, new CellPos(0, 3, 15)), new ConstructChange(_id, 6, false)).Dirty;

        Assert.Equal(set, clear);
        Assert.Equal(3, clear.Count);
    }

    [Fact]
    public void BlockClear_OfLastBlock_ReportsRemoval_WithoutDirtyChunks()
    {
        var (dirty, created, removed, _) = Run(new BlockClearOp(_id, new CellPos(1, 1, 1)), new ConstructChange(_id, 9, true));

        Assert.Equal([_id], removed);
        Assert.Empty(dirty);
        Assert.Empty(created);
    }

    [Fact]
    public void ConstructCreate_ReportsCreated()
    {
        var op = new ConstructCreateOp(_id, FrameRef.Of(new PlanetId(1)), Vector3.Zero, Quaternion.Identity, new CellPos(0, 0, 0), _stone);

        var (dirty, created, removed, _) = Run(op, new ConstructChange(_id, 1, false));

        Assert.Equal([_id], created);
        Assert.Empty(dirty);
        Assert.Empty(removed);
    }

    [Fact]
    public void EntityReparent_ReportsPoseRefresh_WithoutMeshWork()
    {
        var op = new EntityReparentOp(_id, FrameRef.Of(new PlanetId(1)), 1, Vector3.One, Quaternion.Identity, Vector3.Zero, null);

        var (dirty, created, removed, reposed) = Run(op);

        Assert.Equal([_id], reposed);
        Assert.Empty(dirty);
        Assert.Empty(created);
        Assert.Empty(removed);
    }

    [Fact]
    public void TerrainAndPlayerOperations_ProduceNothing()
    {
        var terrain = new TerrainEditOp(new PlanetId(1), TerrainShape.Sphere, Vector3.Zero, 1f, TerrainEditMode.Remove, 0);
        var leave = new PlayerLeaveOp(new PlayerId(1), FrameRef.Of(new PlanetId(1)), 0, Vector3.Zero, Quaternion.Identity, 0f);

        foreach (var op in new Operation[] { terrain, leave })
        {
            var (dirty, created, removed, reposed) = Run(op);
            Assert.Empty(dirty);
            Assert.Empty(created);
            Assert.Empty(removed);
            Assert.Empty(reposed);
        }
    }

    private static ItemCreateOp ItemOp() => new(new SlotRef(new ContainerId(1), 0), new ItemTypeId(1), 1, 500, ItemSource.Admin);

    private static CompoundOp PlaceChest(Operation constructPart) => new(
    [
        new ContainerCreateOp(
            new ContainerId(9), ContainerKind.Chest, ContainerHost.ConstructBlock(_id, new CellPos(20, 8, 40)), new PlayerId(1), ContainerAccess.Shared, 4),
        new BlockEntitySetOp(_id, new CellPos(20, 8, 40), 2, new ChestBlockState(new ContainerId(9))),
        constructPart,
    ]);

    [Fact]
    public void Collect_FunctionStateSet_DirtiesCell()
    {
        var inside = Run(new BlockFunctionStateSetOp(_id, new CellPos(20, 8, 40), 5, 1), new ConstructChange(_id, 6, false));
        var onFace = Run(new BlockFunctionStateSetOp(_id, new CellPos(15, 8, 8), 5, 1), new ConstructChange(_id, 7, false));

        // An open door stops hiding the faces of its neighbours, so a cell on a chunk face dirties the chunk across it as well (as BlockSet does).
        Assert.Equal([new ChunkPos(1, 0, 2)], Chunks(inside.Dirty));
        Assert.Equal([new ChunkPos(0, 0, 0), new ChunkPos(1, 0, 0)], Chunks(onFace.Dirty));
        Assert.Empty(inside.Created);
        Assert.Empty(inside.Removed);
    }

    [Fact]
    public void Collect_CompoundWithBlockSet_DirtiesCell()
    {
        var placed = Run(PlaceChest(new BlockSetOp(_id, new CellPos(20, 8, 40), BlockState.Create(2, 0))), new ConstructChange(_id, 5, false));
        var created = Run(PlaceChest(new ConstructCreateOp(_id, FrameRef.Of(new PlanetId(1)), Vector3.Zero, Quaternion.Identity, new CellPos(20, 8, 40), _stone)));
        var cleared = Run(
            new CompoundOp([new ContainerRemoveOp(new ContainerId(9)), new BlockEntitySetOp(_id, new CellPos(20, 8, 40), 2, null), new BlockClearOp(_id, new CellPos(20, 8, 40))]),
            new ConstructChange(_id, 6, false));
        var doorColumn = Run(
            new CompoundOp(
            [
                new BlockFunctionStateSetOp(_id, new CellPos(3, 15, 0), 5, 1),
                new BlockFunctionStateSetOp(_id, new CellPos(3, 16, 0), 5, 1),
            ]),
            new ConstructChange(_id, 7, false));

        Assert.Equal([new ChunkPos(1, 0, 2)], Chunks(placed.Dirty));
        Assert.Equal([_id], created.Created);
        Assert.Equal([new ChunkPos(1, 0, 2)], Chunks(cleared.Dirty));
        Assert.Equal([new ChunkPos(0, 0, 0), new ChunkPos(0, 1, 0)], Chunks(doorColumn.Dirty).Distinct().ToArray());
    }

    [Fact]
    public void Collect_ItemOnlyCompound_Nothing()
    {
        var transfer = new ItemTransferOp(new SlotRef(new ContainerId(1), 0), new SlotRef(new ContainerId(2), 3), new ItemTypeId(1), 1, 500);
        var itemsOnly = Run(new CompoundOp([ItemOp(), transfer]), new ConstructChange(_id, 5, false));
        var withBlock = Run(new CompoundOp([ItemOp(), new BlockSetOp(_id, new CellPos(20, 8, 40), _stone)]), new ConstructChange(_id, 5, false));

        // The items of a compound move nothing in a construct; the control shows the construct component of the same shape is found.
        Assert.Equal([new ChunkPos(1, 0, 2)], Chunks(withBlock.Dirty));
        Assert.Empty(itemsOnly.Dirty);
        Assert.Empty(itemsOnly.Created);
        Assert.Empty(itemsOnly.Removed);
        Assert.Empty(itemsOnly.Reposed);
    }
}
