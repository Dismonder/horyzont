using Horyzont.Core.BlockEntities;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.World;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Checkpoints;

/// <summary>K6-30: block states and respawn points travel through the checkpoint, the requeue, the builder and the journal tail.</summary>
public sealed class BlockEntityCheckpointTests
{
    [Fact]
    public void Capture_ChangedBlockEntity_Included()
    {
        var f = new FunctionalBlockFixture();
        var first = Complete(f);
        f.Apply(SetBedState(FunctionalBlocks.BedCell, Player1));

        var second = f.State.CaptureCheckpoint();

        Assert.Empty(first.UpsertedBlockEntities);
        var record = Assert.Single(second.UpsertedBlockEntities);
        Assert.Equal(new BlockEntityRecord(ConstructId, FunctionalBlocks.BedCell, new BedBlockState(Player1)), record);
        Assert.Empty(second.RemovedBlockEntities);
        Assert.Empty(second.UpsertedProgress);
    }

    [Fact]
    public void Capture_RemovedBlockEntity_ListedAsRemoved()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetBedState(FunctionalBlocks.BedCell, Player1));
        var first = Complete(f);
        Assert.Single(first.UpsertedBlockEntities);

        f.Apply(ClearState(FunctionalBlocks.BedCell, FunctionalBlocks.BedType));
        var second = f.State.CaptureCheckpoint();

        Assert.Empty(second.UpsertedBlockEntities);
        Assert.Equal([new BlockRef(ConstructId, FunctionalBlocks.BedCell)], second.RemovedBlockEntities);
    }

    [Fact]
    public void Capture_ChangedProgress_Included()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetBedState(FunctionalBlocks.BedCell, Player1));
        Complete(f);
        f.Apply(new RespawnPointSetOp(Player1, BedAt(FunctionalBlocks.BedCell)));

        var second = f.State.CaptureCheckpoint();
        f.State.CompleteCheckpoint(second);
        f.Apply(new RespawnPointSetOp(Player1, null));
        var third = f.State.CaptureCheckpoint();

        Assert.Equal(new PlayerProgressRecord(Player1, BedAt(FunctionalBlocks.BedCell)), Assert.Single(second.UpsertedProgress));
        Assert.Empty(second.RemovedProgress);
        Assert.Empty(third.UpsertedProgress);
        Assert.Equal([Player1], third.RemovedProgress);
    }

    [Fact]
    public void Requeue_MarksBlockEntitiesAndProgressAgain()
    {
        var f = new FunctionalBlockFixture();
        Complete(f);
        f.Apply(SetBedState(FunctionalBlocks.BedCell, Player1));
        f.Apply(new RespawnPointSetOp(Player1, BedAt(FunctionalBlocks.BedCell)));
        var failed = f.State.CaptureCheckpoint();
        f.State.RequeueAfterFailedCheckpoint(failed);

        var retry = f.State.CaptureCheckpoint();

        Assert.Single(failed.UpsertedBlockEntities);
        Assert.Single(failed.UpsertedProgress);
        Assert.Equal(failed.UpsertedBlockEntities, retry.UpsertedBlockEntities);
        Assert.Equal(failed.UpsertedProgress, retry.UpsertedProgress);
    }

    [Fact]
    public void Builder_RestoresBlockEntitiesAndProgress()
    {
        var f = FullWorld(out var checkpoint);

        var loaded = Load(f.State, checkpoint);

        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(loaded));
        Assert.Equal(f.State.BlockEntities.Count, loaded.BlockEntities.Count);
        Assert.Equal(2, loaded.BlockEntities.Count);
        Assert.True(loaded.BlockEntities.TryGet(new BlockRef(ConstructId, FunctionalBlocks.BedCell), out var bed));
        Assert.Equal(new BedBlockState(Player1), bed);
        Assert.True(loaded.Progress.TryGet(Player1, out var progress));
        Assert.Equal(BedAt(FunctionalBlocks.BedCell), progress.RespawnBed);
        Assert.Equal(f.State.ContainerIds.NextValue, loaded.ContainerIds.NextValue);
    }

    [Fact]
    public void Builder_EntityOnEmptyCell_Throws()
    {
        var f = FullWorld(out var checkpoint);
        Assert.Equal(2, Load(f.State, checkpoint).BlockEntities.Count);

        var onEmptyCell = new BlockEntityRecord(ConstructId, FunctionalBlocks.FreeCell, new BedBlockState(PlayerId.None));
        var onWrongConstruct = new BlockEntityRecord(new EntityId(99), FunctionalBlocks.BedCell, new BedBlockState(PlayerId.None));

        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedBlockEntities = [.. checkpoint.UpsertedBlockEntities, onEmptyCell] }));
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedBlockEntities = [.. checkpoint.UpsertedBlockEntities, onWrongConstruct] }));
    }

    [Fact]
    public void Builder_ContainerHostOnEmptyCell_Throws()
    {
        var f = FullWorld(out var checkpoint);
        var loaded = Load(f.State, checkpoint);
        Assert.Single(loaded.Containers.All, c => c.Host.Kind == ContainerHostKind.ConstructBlock);

        var chest = checkpoint.UpsertedContainers.Single(c => c.Host.Kind == ContainerHostKind.ConstructBlock);
        var moved = chest with { Host = ContainerHost.ConstructBlock(ConstructId, FunctionalBlocks.FreeCell) };
        var others = checkpoint.UpsertedContainers.Where(c => c.Id != chest.Id);

        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedContainers = [.. others, moved] }));
    }

    [Fact]
    public void CheckpointPlusJournal_SameHash()
    {
        var f = new FunctionalBlockFixture(players: 2);
        var checkpoint = Complete(f);
        var container = f.State.ContainerIds.PeekNext();
        f.Apply(CompoundConstructTests.PlaceChestCompound(container, FunctionalBlocks.FreeCell));
        f.Apply(SetBedState(FunctionalBlocks.BedCell, Player1));
        f.Apply(new RespawnPointSetOp(Player1, BedAt(FunctionalBlocks.BedCell)));
        f.Apply(new CompoundOp(
        [
            SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1),
            SetFunctionState(FunctionalBlocks.DoorHighCell, FunctionalBlocks.DoorType, 1),
        ]));
        f.Apply(new ContainerConfigureOp(container, 8, ContainerAccess.Private));
        f.Apply(new CompoundOp(
        [
            new ContainerRemoveOp(container),
            ClearState(FunctionalBlocks.FreeCell, FunctionalBlocks.ChestType),
            new BlockClearOp(ConstructId, FunctionalBlocks.FreeCell),
        ]));
        var tail = f.Sim.Journal.Entries.Where(e => e.Id > checkpoint.BarrierOpId).ToList();
        Assert.Equal(6, tail.Count);

        var loaded = Load(f.State, checkpoint);
        var replay = new SimFixture(state: loaded);
        foreach (var entry in tail)
        {
            replay.Sim.Replay(entry);
        }

        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(replay.State));
        Assert.Equal(1, replay.State.BlockEntities.Count);
        Assert.True(replay.State.Progress.TryGet(Player1, out _));
    }

    private static WorldCheckpoint Complete(FunctionalBlockFixture f)
    {
        var checkpoint = f.State.CaptureCheckpoint();
        f.State.CompleteCheckpoint(checkpoint);
        return checkpoint;
    }

    /// <summary>A world with a chest (container and state), an owned bed and its respawn point, and the one full checkpoint of it.</summary>
    private static FunctionalBlockFixture FullWorld(out WorldCheckpoint checkpoint)
    {
        var f = new FunctionalBlockFixture();
        f.SetUpChest(FunctionalBlocks.ChestCell);
        f.Apply(SetBedState(FunctionalBlocks.BedCell, Player1));
        f.Apply(new RespawnPointSetOp(Player1, BedAt(FunctionalBlocks.BedCell)));
        checkpoint = f.State.CaptureCheckpoint();
        return f;
    }

    /// <summary>A world from one full checkpoint (the first capture of a world contains every construct, container, player and state).</summary>
    private static WorldState Load(WorldState live, WorldCheckpoint checkpoint)
    {
        var builder = new WorldStateBuilder(live.Id, live.GalaxySeed);
        builder.SetProgress(checkpoint.BarrierOpId, checkpoint.Tick, checkpoint.NextEntityId, checkpoint.NextPlayerId);
        builder.SetNextContainerId(checkpoint.NextContainerId);
        foreach (var planet in live.Planets.All)
        {
            builder.AddPlanet(planet);
        }

        foreach (var entry in live.BlockTypes.Entries)
        {
            builder.AddBlockType(entry);
        }

        foreach (var entry in live.Materials.Entries)
        {
            builder.AddMaterial(entry);
        }

        foreach (var entry in live.ItemTypes.Entries)
        {
            builder.AddItemType(entry);
        }

        foreach (var header in checkpoint.UpsertedConstructs)
        {
            builder.AddConstruct(header);
        }

        foreach (var chunk in checkpoint.UpsertedChunks)
        {
            builder.AddChunk(chunk);
        }

        foreach (var player in checkpoint.UpsertedPlayers)
        {
            builder.AddPlayer(player);
        }

        foreach (var container in checkpoint.UpsertedContainers)
        {
            builder.AddContainer(container);
        }

        foreach (var entity in checkpoint.UpsertedBlockEntities)
        {
            builder.AddBlockEntity(entity);
        }

        foreach (var progress in checkpoint.UpsertedProgress)
        {
            builder.AddPlayerProgress(progress);
        }

        foreach (var version in checkpoint.TerrainVersions)
        {
            builder.AddTerrainVersion(version);
        }

        return builder.Build();
    }
}
