using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.World;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Items.ItemFixture;

namespace Horyzont.Core.Tests.Checkpoints;

public sealed class ContainerCheckpointTests
{
    [Fact]
    public void Capture_ChangedContainer_Included()
    {
        var f = new ItemFixture();
        var first = f.State.CaptureCheckpoint();
        f.State.CompleteCheckpoint(first);
        f.Apply(Create(BagSlot(2), Stone, 9));

        var second = f.State.CaptureCheckpoint();

        Assert.Equal([Bag, Hotbar, WorldFactory.EquipmentOf(1)], first.UpsertedContainers.Select(c => c.Id));
        Assert.Equal(4UL, first.NextContainerId);
        var bag = Assert.Single(second.UpsertedContainers);
        Assert.Equal(Bag, bag.Id);
        Assert.Equal(f.Container(Bag).Version, bag.Version);
        Assert.Equal(WorldFactory.BagSize, bag.Slots.Count);
        Assert.Equal(new ItemStack(Stone, 9), bag.Slots[2]);
        Assert.Empty(second.RemovedContainers);
        Assert.Equal(4UL, second.NextContainerId);
    }

    [Fact]
    public void Capture_RefusedCompound_MarksNothing()
    {
        var f = new ItemFixture();
        f.State.CompleteCheckpoint(f.State.CaptureCheckpoint());
        var player = new PlayerId(1);

        f.AssertFails(new CompoundOp(
        [
            Create(BagSlot(2), Stone, 9),
            new ContainerCreateOp(new ContainerId(4), ContainerKind.Chest, ContainerHost.Player(player), player, ContainerAccess.Shared, 2),
            Destroy(BagSlot(3), Stone, 1),
        ]));

        Assert.Empty(f.State.CaptureCheckpoint().UpsertedContainers);
    }

    [Fact]
    public void Requeue_MarksContainersAgain()
    {
        var f = new ItemFixture();
        f.State.CompleteCheckpoint(f.State.CaptureCheckpoint());
        f.Apply(Create(HotbarSlot(0), Tool, 1, limit: 1));
        var failed = f.State.CaptureCheckpoint();
        f.State.RequeueAfterFailedCheckpoint(failed);

        var retry = f.State.CaptureCheckpoint();

        Assert.Equal([Hotbar], failed.UpsertedContainers.Select(c => c.Id));
        Assert.Equal(failed.UpsertedContainers.Select(c => c.Id), retry.UpsertedContainers.Select(c => c.Id));
        Assert.Equal(failed.UpsertedContainers[0].Slots, retry.UpsertedContainers[0].Slots);
    }

    [Fact]
    public void Builder_RestoresContainersAndNextContainerId()
    {
        var f = new ItemFixture(players: 2);
        f.Apply(Create(BagSlot(5), Gem, 4, limit: 5));
        f.Apply(Transfer(BagSlot(5), new SlotRef(WorldFactory.HotbarOf(2), 9), Gem, 1, limit: 5));
        var checkpoint = f.State.CaptureCheckpoint();

        var loaded = Load(f.State, checkpoint);

        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(loaded));
        Assert.Equal(f.State.ContainerIds.NextValue, loaded.ContainerIds.NextValue);
        Assert.True(loaded.Containers.TryGetOfPlayer(new PlayerId(2), ContainerKind.Hotbar, out var hotbar));
        Assert.Equal(new ItemStack(Gem, 1), hotbar.GetSlot(9));
    }

    [Fact]
    public void Builder_InconsistentContainers_Throw()
    {
        var f = new ItemFixture();
        var checkpoint = f.State.CaptureCheckpoint();
        var bag = checkpoint.UpsertedContainers[0];

        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { NextContainerId = 3 }));
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedContainers = [.. checkpoint.UpsertedContainers, bag] }));
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with
        {
            UpsertedContainers = [.. checkpoint.UpsertedContainers, bag with { Id = new ContainerId(3) }],
        }));
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with
        {
            UpsertedContainers = [bag with { Slots = [new ItemStack(new ItemTypeId(99), 1), .. bag.Slots.Skip(1)] }],
        }));
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with
        {
            UpsertedContainers = [bag with { Host = ContainerHost.Player(new PlayerId(7)) }],
        }));
    }

    [Fact]
    public void CheckpointPlusCompoundJournal_SameHash()
    {
        var f = new ItemFixture(players: 2);
        f.Apply(Create(BagSlot(0), Stone, 20, source: ItemSource.Harvest));
        var checkpoint = f.State.CaptureCheckpoint();
        f.State.CompleteCheckpoint(checkpoint);
        var second = new PlayerId(2);
        var chest = f.State.ContainerIds.PeekNext();

        f.Apply(new CompoundOp(
        [
            Transfer(BagSlot(0), HotbarSlot(3), Stone, 7),
            Create(BagSlot(1), Soil, 3, source: ItemSource.Harvest),
            CompoundOpTests.Dig(f),
        ]));
        f.Apply(new CompoundOp(
        [
            new ContainerCreateOp(chest, ContainerKind.Chest, ContainerHost.Player(second), second, ContainerAccess.Shared, 4),
            Transfer(BagSlot(0), new SlotRef(chest, 3), Stone, 13),
            Destroy(BagSlot(1), Soil, 3, ItemSink.BuildCost),
        ]));
        var tail = f.Sim.Journal.Entries.Where(e => e.Id > checkpoint.BarrierOpId).ToList();
        Assert.Equal(2, tail.Count);

        var loaded = Load(f.State, checkpoint);
        var replay = new SimFixture(state: loaded);
        foreach (var entry in tail)
        {
            replay.Sim.Replay(entry);
        }

        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(loaded));
        Assert.Equal(f.Sim.Terrain.AppliedEdits, replay.Terrain.AppliedEdits);
    }

    /// <summary>A world from one full checkpoint (the first capture of a world contains every container and online player).</summary>
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

        foreach (var player in checkpoint.UpsertedPlayers)
        {
            builder.AddPlayer(player);
        }

        foreach (var container in checkpoint.UpsertedContainers)
        {
            builder.AddContainer(container);
        }

        foreach (var version in checkpoint.TerrainVersions)
        {
            builder.AddTerrainVersion(version);
        }

        return builder.Build();
    }
}
