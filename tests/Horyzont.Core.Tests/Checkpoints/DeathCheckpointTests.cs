using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Survival;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Checkpoints;

/// <summary>K6-35: tombstones and the settings of the world travel through the checkpoint, the builder and the journal tail (D12, D22).</summary>
public sealed class DeathCheckpointTests
{
    private static readonly PlayerId _p1 = DeathWorld.Player1;

    private static DeathWorld Armed()
    {
        var w = new DeathWorld(survival: true);
        w.GiveKit(_p1);
        return w;
    }

    private static WorldCheckpoint Complete(DeathWorld w)
    {
        var checkpoint = w.State.CaptureCheckpoint();
        w.State.CompleteCheckpoint(checkpoint);
        return checkpoint;
    }

    [Fact]
    public void Capture_Death_UpsertsTheTombstoneAndItsContainer()
    {
        var w = Armed();
        Complete(w);

        w.Die(_p1, DeathPenalty.Hard);
        var checkpoint = w.State.CaptureCheckpoint();

        var entity = Assert.Single(checkpoint.UpsertedPersistentEntities);
        Assert.Equal(PersistentEntityKind.Tombstone, entity.Kind);
        Assert.Equal(_p1, entity.Owner);
        var container = Assert.Single(checkpoint.UpsertedContainers, c => c.Kind == ContainerKind.Tombstone);
        Assert.Equal(ContainerHost.PersistentEntity(entity.Id), container.Host);
        Assert.Empty(checkpoint.RemovedPersistentEntities);
        Assert.Null(checkpoint.Settings);
    }

    [Fact]
    public void Capture_Recovery_RemovesTheTombstone()
    {
        var w = Armed();
        w.Die(_p1, DeathPenalty.Hard);
        var tombstone = Assert.Single(w.Tombstones());
        var entity = new EntityId(tombstone.Host.Id);
        Complete(w);

        w.Hazard.Blocks.Accept(_p1, new TakeAllIntent(tombstone.Id));
        var checkpoint = w.State.CaptureCheckpoint();

        Assert.Equal([entity], checkpoint.RemovedPersistentEntities);
        Assert.Empty(checkpoint.UpsertedPersistentEntities);
        Assert.Equal([tombstone.Id], checkpoint.RemovedContainers);
    }

    [Fact]
    public void Capture_Settings_OnlyWhenChanged_AndRequeuedAfterAFailedWrite()
    {
        var w = new DeathWorld();
        Assert.Null(Complete(w).Settings);
        var settings = new WorldSettings { DeathPenalty = DeathPenalty.Hard, FreeFuel = true };

        w.Apply(new WorldSettingsSetOp(settings));
        var checkpoint = w.State.CaptureCheckpoint();
        Assert.Equal(settings, checkpoint.Settings);
        w.State.RequeueAfterFailedCheckpoint(checkpoint);

        var again = w.State.CaptureCheckpoint();
        Assert.Equal(settings, again.Settings);
        w.State.CompleteCheckpoint(again);
        Assert.Null(w.State.CaptureCheckpoint().Settings);
    }

    [Fact]
    public void Builder_RestoresTombstonesAndSettings()
    {
        var w = Armed();
        w.Apply(new WorldSettingsSetOp(new WorldSettings { DeathPenalty = DeathPenalty.Mild, StackMultiplierPercent = 150 }));
        w.Die(_p1, DeathPenalty.Hard);
        var checkpoint = w.State.CaptureCheckpoint();

        var loaded = Load(w.State, checkpoint);

        Assert.Equal(StateHasher.Hash(w.State), StateHasher.Hash(loaded));
        var tombstone = Assert.Single(w.Tombstones());
        Assert.True(loaded.Containers.TryGet(tombstone.Id, out var restored));
        Assert.Equal(tombstone.Slots.ToArray(), restored.Slots.ToArray());
        Assert.True(loaded.PersistentEntities.TryGet(new EntityId(tombstone.Host.Id), out var entity));
        Assert.Equal([entity], loaded.PersistentEntities.OwnedBy(_p1));
        Assert.Equal(w.State.Settings, loaded.Settings);
        Assert.True(loaded.Containers.TryGetOfEntity(entity.Id, out var hosted) && hosted.Id == tombstone.Id);
    }

    [Fact]
    public void CheckpointPlusReplay_DeathAndRecovery_SameHash()
    {
        var w = Armed();
        var checkpoint = Complete(w);
        w.Apply(new WorldSettingsSetOp(new WorldSettings { DeathPenalty = DeathPenalty.Hard }));
        w.Die(_p1, DeathPenalty.Hard);
        var tombstone = Assert.Single(w.Tombstones());
        w.Hazard.Blocks.Accept(
            _p1, new ItemMoveIntent(new SlotRef(tombstone.Id, 0), ItemMoveTarget.AnyIn(DeathWorld.BagOf(_p1)), tombstone.GetSlot(0).Count, tombstone.GetSlot(0).Type, tombstone.GetSlot(0).Count));
        var tail = w.Hazard.Blocks.Sim.Journal.Entries.Where(e => e.Id > checkpoint.BarrierOpId).ToList();
        Assert.True(tail.Count >= 3, $"{tail.Count} operations in the tail");

        var loaded = Load(w.State, checkpoint);
        var replay = new Horyzont.Core.Tests.SimFixture(state: loaded);
        foreach (var entry in tail)
        {
            replay.Sim.Replay(entry);
        }

        Assert.Equal(StateHasher.Hash(w.State), StateHasher.Hash(replay.State));
        Assert.Equal(1, replay.State.PersistentEntities.Count);
        Assert.Equal(DeathPenalty.Hard, replay.State.Settings.DeathPenalty);
    }

    [Fact]
    public void Builder_OrphanOrMismatchedEntity_Throws()
    {
        var w = Armed();
        w.Die(_p1, DeathPenalty.Hard);
        var checkpoint = w.State.CaptureCheckpoint();
        Load(w.State, checkpoint);
        var entity = Assert.Single(checkpoint.UpsertedPersistentEntities);
        var container = Assert.Single(checkpoint.UpsertedContainers, c => c.Kind == ContainerKind.Tombstone);

        // No container for the entity.
        Assert.Throws<InvalidDataException>(() => Load(w.State, checkpoint with { UpsertedContainers = [.. checkpoint.UpsertedContainers.Where(c => c.Id != container.Id)] }));

        // A container of another owner, in another access, of another kind, or hosted by an entity that does not exist.
        Assert.Throws<InvalidDataException>(() => Load(w.State, With(checkpoint, container with { Owner = DeathWorld.Player2 })));
        Assert.Throws<InvalidDataException>(() => Load(w.State, With(checkpoint, container with { Access = ContainerAccess.Shared })));
        Assert.Throws<InvalidDataException>(() => Load(w.State, With(checkpoint, container with { Kind = ContainerKind.DroppedItem })));
        Assert.Throws<InvalidDataException>(() => Load(w.State, With(checkpoint, container with { Host = ContainerHost.PersistentEntity(new EntityId(entity.Id.Value + 50)) })));

        // An entity with an unknown owner, in the frame of a construct or of no planet, with a pose that is not finite, with the id of a construct, or beyond NextEntityId.
        Assert.Throws<InvalidDataException>(() => Load(w.State, With(checkpoint, entity with { Owner = new PlayerId(77) })));
        Assert.Throws<InvalidDataException>(() => Load(w.State, With(checkpoint, entity with { Frame = FrameRef.Of(FunctionalBlocks.Construct) })));
        Assert.Throws<InvalidDataException>(() => Load(w.State, With(checkpoint, entity with { Frame = FrameRef.Of(new PlanetId(99)) })));
        Assert.Throws<InvalidDataException>(() => Load(w.State, With(checkpoint, entity with { Position = new System.Numerics.Vector3(float.NaN, 0, 0) })));
        Assert.Throws<InvalidDataException>(() => Load(w.State, With(checkpoint, entity with { Id = FunctionalBlocks.Construct })));
        Assert.Throws<InvalidDataException>(() => Load(w.State, With(checkpoint, entity with { Id = new EntityId(checkpoint.NextEntityId) })));
        Assert.Throws<InvalidDataException>(() => Load(w.State, With(checkpoint, entity with { Kind = (PersistentEntityKind)9 })));
    }

    [Fact]
    public void Builder_SettingsOutOfRange_Throws()
    {
        var w = new DeathWorld();
        var checkpoint = w.State.CaptureCheckpoint();

        Assert.Throws<InvalidDataException>(() => Load(w.State, checkpoint with { Settings = new WorldSettings { ResourceMultiplierPercent = 10 } }));
        Load(w.State, checkpoint with { Settings = new WorldSettings { ResourceMultiplierPercent = 120 } });
    }

    private static WorldCheckpoint With(WorldCheckpoint checkpoint, PersistentEntity entity) =>
        checkpoint with { UpsertedPersistentEntities = [entity] };

    private static WorldCheckpoint With(WorldCheckpoint checkpoint, ContainerRecord container) =>
        checkpoint with { UpsertedContainers = [.. checkpoint.UpsertedContainers.Select(c => c.Id == container.Id ? container : c)] };

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

        foreach (var entity in checkpoint.UpsertedPersistentEntities)
        {
            builder.AddPersistentEntity(entity);
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

        if (checkpoint.ProgressKeys is { } keys)
        {
            builder.SetProgressKeys(keys);
        }

        if (checkpoint.Settings is { } settings)
        {
            builder.SetWorldSettings(settings);
        }

        foreach (var version in checkpoint.TerrainVersions)
        {
            builder.AddTerrainVersion(version);
        }

        return builder.Build();
    }
}
