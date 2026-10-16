using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.Tests.Props;
using Horyzont.Core.World;

using static Horyzont.Core.Tests.Props.PropFixture;

namespace Horyzont.Core.Tests.Checkpoints;

/// <summary>K6-31 D16: harvested props travel through the checkpoint, the requeue, the builder and the journal tail.</summary>
public sealed class HarvestedPropCheckpointTests
{
    [Fact]
    public void Capture_Harvested_Included()
    {
        var f = new PropFixture();
        var first = Complete(f);
        f.Apply(Harvest(Tree(0), 120));
        f.Apply(Harvest(Rock(0), 130));

        var second = f.State.CaptureCheckpoint();

        Assert.Empty(first.UpsertedHarvestedProps);
        Assert.Equal([new HarvestedPropRecord(Planet, Tree(0), new Tick(120)), new HarvestedPropRecord(Planet, Rock(0), new Tick(130))], second.UpsertedHarvestedProps);
        Assert.Empty(second.RemovedHarvestedProps);

        // Only the changes since the previous checkpoint are listed.
        f.State.CompleteCheckpoint(second);
        f.Apply(Harvest(Tree(1), 140));
        var third = f.State.CaptureCheckpoint();
        Assert.Equal([new HarvestedPropRecord(Planet, Tree(1), new Tick(140))], third.UpsertedHarvestedProps);
    }

    [Fact]
    public void Capture_Regrown_ListedAsRemoved()
    {
        var f = new PropFixture();
        f.Apply(Harvest(Tree(0)));
        f.Apply(Harvest(Tree(1)));
        var first = Complete(f);
        Assert.Equal(2, first.UpsertedHarvestedProps.Count);

        f.Apply(Regrow(Tree(0)));
        var second = f.State.CaptureCheckpoint();

        Assert.Empty(second.UpsertedHarvestedProps);
        Assert.Equal([new HarvestedPropKey(Planet, Tree(0))], second.RemovedHarvestedProps);
    }

    [Fact]
    public void Capture_HarvestThenRegrowInOneWindow_NotUpserted()
    {
        var f = new PropFixture();
        Complete(f);
        f.Apply(Harvest(Tree(0), 120));
        f.Apply(Harvest(Tree(1), 121));
        f.Apply(Regrow(Tree(0)));

        var checkpoint = f.State.CaptureCheckpoint();

        // The whole prop never reached a checkpoint; deleting a row that does not exist is harmless, so it may appear among the removed.
        Assert.Equal([new HarvestedPropRecord(Planet, Tree(1), new Tick(121))], checkpoint.UpsertedHarvestedProps);
        Assert.All(checkpoint.RemovedHarvestedProps, key => Assert.Equal(new HarvestedPropKey(Planet, Tree(0)), key));
    }

    [Fact]
    public void Capture_RegrowThenHarvestInOneWindow_UpsertedWithNewTick()
    {
        var f = new PropFixture();
        f.Apply(Harvest(Tree(0), 120));
        Complete(f);
        f.Apply(Regrow(Tree(0)));
        f.Apply(Harvest(Tree(0), 400));

        var checkpoint = f.State.CaptureCheckpoint();

        Assert.Equal([new HarvestedPropRecord(Planet, Tree(0), new Tick(400))], checkpoint.UpsertedHarvestedProps);
        Assert.Empty(checkpoint.RemovedHarvestedProps);
    }

    [Fact]
    public void Requeue_MarksAgain()
    {
        var f = new PropFixture();
        Complete(f);
        f.Apply(Harvest(Tree(0), 120));
        f.Apply(Harvest(Tree(1), 121));
        var failed = f.State.CaptureCheckpoint();
        f.State.RequeueAfterFailedCheckpoint(failed);

        var retry = f.State.CaptureCheckpoint();

        Assert.Equal(2, failed.UpsertedHarvestedProps.Count);
        Assert.Equal(failed.UpsertedHarvestedProps, retry.UpsertedHarvestedProps);
        Assert.Equal(failed.RemovedHarvestedProps, retry.RemovedHarvestedProps);
    }

    [Fact]
    public void Builder_RestoresHarvested()
    {
        var f = FullWorld(out var checkpoint);

        var loaded = Load(f.State, checkpoint);

        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(loaded));
        Assert.Equal(f.State.HarvestedProps.All, loaded.HarvestedProps.All);
        Assert.Equal(3, loaded.HarvestedProps.Count);
        Assert.True(loaded.HarvestedProps.TryGetTick(Planet, Rock(0), out var tick));
        Assert.Equal(new Tick(130), tick);
    }

    [Fact]
    public void Builder_UnknownPlanet_Throws()
    {
        var f = FullWorld(out var checkpoint);
        Assert.Equal(3, Load(f.State, checkpoint).HarvestedProps.Count);
        var stranger = new HarvestedPropRecord(new PlanetId(99), Tree(5), new Tick(1));

        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedHarvestedProps = [.. checkpoint.UpsertedHarvestedProps, stranger] }));
    }

    [Fact]
    public void Builder_DuplicateKey_Throws()
    {
        var f = FullWorld(out var checkpoint);
        Assert.Equal(3, Load(f.State, checkpoint).HarvestedProps.Count);
        var again = checkpoint.UpsertedHarvestedProps[0] with { Tick = new Tick(999) };

        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedHarvestedProps = [.. checkpoint.UpsertedHarvestedProps, again] }));
    }

    [Fact]
    public void Builder_CellOutsidePlanetGrid_Throws()
    {
        var f = FullWorld(out var checkpoint);
        Assert.Equal(3, Load(f.State, checkpoint).HarvestedProps.Count);

        // The home planet has 784 cells per face (radius 4000 m), so cell 783 is the last one; 784 is outside, though the identifier itself is valid.
        var last = new HarvestedPropRecord(Planet, Raw(2, 783, 783, 0), new Tick(1));
        var outsideU = new HarvestedPropRecord(Planet, Raw(2, 784, 0, 0), new Tick(1));
        var outsideV = new HarvestedPropRecord(Planet, Raw(2, 0, 800, 1), new Tick(1));
        var reserved = new HarvestedPropRecord(Planet, new PropId(Tree(7).Value | 1UL), new Tick(1));
        var badFace = new HarvestedPropRecord(Planet, new PropId(6UL << 58), new Tick(1));

        Assert.Equal(4, Load(f.State, checkpoint with { UpsertedHarvestedProps = [.. checkpoint.UpsertedHarvestedProps, last] }).HarvestedProps.Count);
        foreach (var bad in new[] { outsideU, outsideV, reserved, badFace })
        {
            Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedHarvestedProps = [.. checkpoint.UpsertedHarvestedProps, bad] }));
        }
    }

    [Fact]
    public void CheckpointPlusJournal_SameHash()
    {
        var f = new PropFixture(players: 2);
        f.Apply(Harvest(Tree(0), 50));
        var checkpoint = Complete(f);
        f.Apply(Harvest(Tree(1), 60));
        f.Apply(Regrow(Tree(0)));
        f.Apply(new CompoundOp([Harvest(Rock(0), 70), Harvest(Tree(2), 70), ItemFixture.Create(ItemFixture.BagSlot(0), ItemFixture.Stone, 3, source: ItemSource.Harvest)]));
        f.Apply(new CompoundOp([Regrow(Tree(2)), Harvest(Tree(0), 80)]));
        var tail = f.Sim.Journal.Entries.Where(e => e.Id > checkpoint.BarrierOpId).ToList();
        Assert.Equal(4, tail.Count);

        var loaded = Load(f.State, checkpoint);
        var replay = new SimFixture(state: loaded);
        foreach (var entry in tail)
        {
            replay.Sim.Replay(entry);
        }

        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(replay.State));
        Assert.Equal(f.State.HarvestedProps.All, replay.State.HarvestedProps.All);
        Assert.Equal(3, replay.State.HarvestedProps.Count);
    }

    private static WorldCheckpoint Complete(PropFixture f)
    {
        var checkpoint = f.State.CaptureCheckpoint();
        f.State.CompleteCheckpoint(checkpoint);
        return checkpoint;
    }

    /// <summary>A world with three harvested props (two trees and a rock) and the one full checkpoint of it.</summary>
    private static PropFixture FullWorld(out WorldCheckpoint checkpoint)
    {
        var f = new PropFixture();
        f.Apply(Harvest(Tree(0), 120));
        f.Apply(Harvest(Tree(1), 125));
        f.Apply(Harvest(Rock(0), 130));
        checkpoint = f.State.CaptureCheckpoint();
        return f;
    }

    /// <summary>A world from one full checkpoint (the first capture of a world contains every container, player and harvested prop).</summary>
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

        foreach (var harvested in checkpoint.UpsertedHarvestedProps)
        {
            builder.AddHarvestedProp(harvested);
        }

        foreach (var version in checkpoint.TerrainVersions)
        {
            builder.AddTerrainVersion(version);
        }

        return builder.Build();
    }
}
