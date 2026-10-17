using Horyzont.Core.BlockEntities;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Persistence.Tests;

/// <summary>K6-30 stage C: block states and respawn points go to <c>block_entities</c> and <c>player_progress</c> and come back (tables of M0003).</summary>
public sealed class BlockEntityPersistenceTests
{
    [Fact]
    public async Task Checkpoint_WritesBlockEntitiesAndProgress_LoadRestores()
    {
        using var dir = new TempDirectory();
        ulong hash;
        FunctionalWorld built;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: FunctionalWorldScript.NewWorld()))
        {
            built = FunctionalWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(2L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM block_entities;"));
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM player_progress;"));
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM containers WHERE container_id = " + built.Chest.Value + ";"));

            // The tail: an item into the chest and the lamp switched off, replayed on top of the checkpoint.
            h.Sim.ApplyServerOperation(new ItemCreateOp(
                new SlotRef(built.Chest, 4), WorldFactory.Item(SurvivalContentFactory.GemKey), 2, 5, ItemSource.Admin));
            h.Sim.ApplyServerOperation(new BlockFunctionStateSetOp(built.Construct, FunctionalWorld.LampCell, FunctionalBlocks.LampType, 0));
            await h.FlushAndPump();
            hash = StateHasher.Hash(h.State);
        }

        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.Equal(2, loaded.State.BlockEntities.Count);
        Assert.True(loaded.State.BlockEntities.TryGet(new BlockRef(built.Construct, FunctionalWorld.ChestCell), out var chestState));
        Assert.Equal(new ChestBlockState(built.Chest), chestState);
        Assert.True(loaded.State.BlockEntities.TryGet(new BlockRef(built.Construct, FunctionalWorld.BedCell), out var bedState));
        Assert.Equal(new BedBlockState(built.Player), bedState);
        Assert.True(loaded.State.Progress.TryGet(built.Player, out var progress));
        Assert.Equal(FunctionalWorld.Bed(built.Construct), progress.RespawnBed);
        Assert.Equal(2, loaded.JournalTail.Count);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
        Assert.True(sim.State.Constructs.TryGet(built.Construct, out var construct));
        Assert.Equal(0, construct.GetBlock(FunctionalWorld.LampCell).FunctionState);
        Assert.Equal(1, construct.GetBlock(FunctionalWorld.DoorCell).FunctionState);
    }

    [Fact]
    public async Task Checkpoint_RemovedBlockEntity_DeletesRow()
    {
        using var dir = new TempDirectory();
        FunctionalWorld built;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: FunctionalWorldScript.NewWorld()))
        {
            built = FunctionalWorldScript.Build(h);
            h.Sim.ApplyServerOperation(new ItemDestroyOp(
                new SlotRef(built.Chest, 3), WorldFactory.Item(SurvivalContentFactory.StoneKey), 40, ItemSink.Admin));
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(2L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM block_entities;"));

            h.Sim.ApplyServerOperation(new CompoundOp(
            [
                new ContainerRemoveOp(built.Chest),
                new BlockEntitySetOp(built.Construct, FunctionalWorld.ChestCell, FunctionalBlocks.ChestType, null),
                new BlockClearOp(built.Construct, FunctionalWorld.ChestCell),
            ]));
            Assert.True(await h.CheckpointAndPump());
        }

        Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM block_entities;"));
        Assert.Equal(0L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM containers WHERE container_id = " + built.Chest.Value + ";"));
        Assert.Empty(RawDb.JournalIds(dir.WorldPath));
        var (loaded, _, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        var only = Assert.Single(loaded.State.BlockEntities.All);
        Assert.IsType<BedBlockState>(only.Value);
    }

    [Fact]
    public async Task Checkpoint_ClearedRespawn_DeletesRow()
    {
        using var dir = new TempDirectory();
        FunctionalWorld built;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: FunctionalWorldScript.NewWorld()))
        {
            built = FunctionalWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM player_progress;"));

            h.Sim.ApplyServerOperation(new CompoundOp(
            [
                new RespawnPointSetOp(built.Player, null),
                new BlockEntitySetOp(built.Construct, FunctionalWorld.BedCell, FunctionalBlocks.BedType, new BedBlockState(PlayerId.None)),
            ]));
            Assert.True(await h.CheckpointAndPump());
        }

        Assert.Equal(0L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM player_progress;"));
        Assert.Equal(2L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM block_entities;"));
        var (loaded, _, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        Assert.Equal(0, loaded.State.Progress.Count);
        Assert.True(loaded.State.BlockEntities.TryGet(new BlockRef(built.Construct, FunctionalWorld.BedCell), out var bed));
        Assert.Equal(new BedBlockState(PlayerId.None), bed);
    }

    [Fact]
    public async Task CorruptBlockEntityBlob_WorldCorrupted()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: FunctionalWorldScript.NewWorld()))
        {
            FunctionalWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(2L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM block_entities;"));
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM player_progress;"));
        }

        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "UPDATE block_entities SET data = $data;", ("$data", new byte[] { 21, 1, 0xFF, 0xFF }));

        Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));
    }

    [Fact]
    public async Task BlockEntityRowKeyMismatch_WorldCorrupted()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: FunctionalWorldScript.NewWorld()))
        {
            FunctionalWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(2L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM block_entities;"));
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM player_progress;"));
        }

        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "UPDATE block_entities SET cell_z = cell_z + 5 WHERE cell_x = " + FunctionalWorld.BedCell.X + ";");

        Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));
    }

    [Fact]
    public async Task ProgressRowPlayerMismatch_WorldCorrupted()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: FunctionalWorldScript.NewWorld()))
        {
            FunctionalWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(2L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM block_entities;"));
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM player_progress;"));
        }

        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "UPDATE player_progress SET player_id = 99;");

        Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));
    }
}
