using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Tests.Survival;
using Horyzont.Core.World;

using static Horyzont.Core.Tests.Survival.FoodWorld;

namespace Horyzont.Core.Tests.Checkpoints;

/// <summary>K6-33: food and the vitals of the players travel through the checkpoint, the builder and the journal tail (D4 to D7).</summary>
public sealed class FoodCheckpointTests
{
    private static readonly PlayerVitals _vitals = new(80f, 55.5f, new Tick(36000), false);

    [Fact]
    public void Capture_FoodChanged_UpsertsProgress()
    {
        var f = new FoodWorld();
        Complete(f);
        f.Apply(Set(Player1, Slot(Berries, 100), Slot(Jam, 200)));

        var checkpoint = f.State.CaptureCheckpoint();

        var record = Assert.Single(checkpoint.UpsertedProgress);
        Assert.Equal(Player1, record.Player);
        Assert.Null(record.RespawnBed);
        Assert.Null(record.Discovered);
        Assert.Equal(Slots(Slot(Berries, 100), Slot(Jam, 200)), record.Food);
        Assert.Empty(checkpoint.RemovedProgress);
    }

    [Fact]
    public void Capture_FoodClearedNothingElse_RemovesProgress()
    {
        var f = new FoodWorld();
        f.Apply(Set(Player1, Slot(Berries, 100)));
        Complete(f);

        // The accepted counterpart: a change of the food only upserts the record.
        f.Apply(Set(Player1, Slot(Jam, 200)));
        var changed = f.State.CaptureCheckpoint();
        Assert.Single(changed.UpsertedProgress);
        Assert.Empty(changed.RemovedProgress);
        f.State.CompleteCheckpoint(changed);

        f.Apply(Set(Player1));
        var cleared = f.State.CaptureCheckpoint();

        Assert.Empty(cleared.UpsertedProgress);
        Assert.Equal([Player1], cleared.RemovedProgress);
    }

    [Fact]
    public void Capture_FoodClearedWithBed_UpsertedNotRemoved()
    {
        var f = new FoodWorld();
        f.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell, Player1);
        f.Apply(Set(Player1, Slot(Berries, 100)));
        Complete(f);

        f.Apply(Set(Player1));
        var checkpoint = f.State.CaptureCheckpoint();

        var record = Assert.Single(checkpoint.UpsertedProgress);
        Assert.NotNull(record.RespawnBed);
        Assert.True(record.Food.IsEmpty);
        Assert.Empty(checkpoint.RemovedProgress);
    }

    [Fact]
    public void Capture_OnlinePlayer_CarriesVitals()
    {
        var f = new FoodWorld(players: 2);
        Assert.True(f.Simulation.UpdatePlayerVitals(Player1, _vitals));
        Complete(f);

        // The second capture changes nothing in the journal, yet every online player is in it, with the vitals set meanwhile and without a change mark.
        var later = new PlayerVitals(70f, 20f, default, false);
        Assert.True(f.Simulation.UpdatePlayerVitals(Player1, later));
        var checkpoint = f.State.CaptureCheckpoint();

        var one = Assert.Single(checkpoint.UpsertedPlayers, p => p.Player == Player1);
        Assert.Equal(later, one.Vitals);
        var two = Assert.Single(checkpoint.UpsertedPlayers, p => p.Player == Player2);
        Assert.Null(two.Vitals);
    }

    [Fact]
    public void Capture_OfflinePlayer_KeepsTheLastVitals()
    {
        var f = new FoodWorld();
        Assert.True(f.Simulation.UpdatePlayerVitals(Player1, _vitals));
        var state = f.Blocks.Sim.PlayerOf(Player1);
        f.Simulation.ApplyServerOperation(new PlayerLeaveOp(Player1, state.Frame, state.ParentSeq, state.Position, Quaternion.Identity, 0f));

        // The leave marks the player changed, so the checkpoint has the record, with the vitals of the moment the player left.
        var checkpoint = f.State.CaptureCheckpoint();

        var record = Assert.Single(checkpoint.UpsertedPlayers, p => p.Player == Player1);
        Assert.False(record.IsOnline);
        Assert.Equal(_vitals, record.Vitals);
    }

    [Fact]
    public void Builder_RestoresFoodAndBed()
    {
        var f = FullWorld(out var checkpoint);

        var loaded = Load(f.State, checkpoint);

        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(loaded));
        Assert.True(loaded.Progress.TryGet(Player1, out var first));
        Assert.NotNull(first.RespawnBed);
        Assert.Equal(Slots(Slot(Berries, 100), Slot(Jam, 200)), first.Food);
        Assert.True(loaded.Progress.TryGet(Player2, out var second));
        Assert.Null(second.RespawnBed);
        Assert.Null(second.Discovered);
        Assert.Equal(Slots(default, default, Slot(Bread, 300)), second.Food);
    }

    [Fact]
    public void Builder_EmptyProgressRecord_Throws()
    {
        var f = FullWorld(out var checkpoint);
        Assert.Equal(2, Load(f.State, checkpoint).Progress.Count);

        // A record needs a respawn point, a discovery or food (D4); a record with none of them must not exist.
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedProgress = [new PlayerProgressRecord(Player1, null)] }));
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedProgress = [new PlayerProgressRecord(Player1, null, null, FoodSlots.Empty)] }));
        Load(f.State, checkpoint with { UpsertedProgress = [new PlayerProgressRecord(Player1, null, null, Slots(Slot(Berries, 100)))] });
    }

    [Fact]
    public void Builder_FoodUnknownType_Throws()
    {
        var f = FullWorld(out var checkpoint);
        Assert.Equal(2, Load(f.State, checkpoint).Progress.Count);

        // The registry has the ids 1 to 8.
        var unknown = checkpoint.UpsertedProgress.Select(p => p.Player == Player2 ? p with { Food = Slots(Slot(new ItemTypeId(9), 300)) } : p);
        var beyond = checkpoint.UpsertedProgress.Select(p => p.Player == Player2 ? p with { Food = Slots(Slot(new ItemTypeId(4097), 300)) } : p);

        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedProgress = [.. unknown] }));
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedProgress = [.. beyond] }));
    }

    [Fact]
    public void Builder_FoodDuplicateType_Throws()
    {
        var f = FullWorld(out var checkpoint);
        Assert.Equal(2, Load(f.State, checkpoint).Progress.Count);

        var duplicate = checkpoint.UpsertedProgress.Select(p => p.Player == Player1 ? p with { Food = Slots(Slot(Berries, 100), Slot(Berries, 200)) } : p);

        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedProgress = [.. duplicate] }));
    }

    [Fact]
    public void Builder_EmptyFoodSlotWithTick_Throws()
    {
        var f = FullWorld(out var checkpoint);
        Assert.Equal(2, Load(f.State, checkpoint).Progress.Count);

        var withTick = checkpoint.UpsertedProgress.Select(p => p.Player == Player1 ? p with { Food = Slots(Slot(Berries, 100), new FoodSlot(ItemTypeId.None, new Tick(7))) } : p);

        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedProgress = [.. withTick] }));
    }

    [Fact]
    public void Builder_Vitals_RestoredIntoPlayerState()
    {
        var f = new FoodWorld(players: 2);
        Assert.True(f.Simulation.UpdatePlayerVitals(Player1, _vitals));
        var checkpoint = f.State.CaptureCheckpoint();

        var loaded = Load(f.State, checkpoint);

        Assert.True(loaded.Players.TryGet(Player1, out var one));
        Assert.Equal(_vitals, one.Vitals);
        Assert.True(loaded.Players.TryGet(Player2, out var two));
        Assert.Null(two.Vitals);

        // The vitals are outside the state hash: the loaded world and the live world agree.
        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(loaded));
    }

    [Fact]
    public void Builder_InvalidVitals_Throws()
    {
        var f = new FoodWorld();
        Assert.True(f.Simulation.UpdatePlayerVitals(Player1, _vitals));
        var checkpoint = f.State.CaptureCheckpoint();
        Load(f.State, checkpoint);

        foreach (var bad in new[] { float.NaN, -1f, 10_001f, float.PositiveInfinity })
        {
            var players = checkpoint.UpsertedPlayers.Select(p => p with { Vitals = new PlayerVitals(bad, 10f, default, false) });
            Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedPlayers = [.. players] }));
        }
    }

    [Fact]
    public void CheckpointPlusReplay_FoodJournal_SameHash()
    {
        var f = new FoodWorld(players: 2);
        var checkpoint = Complete(f);
        f.GiveFood(Player1, Berries, 3);
        f.GiveFood(Player2, Jam, 1);
        f.Eat(Player1, 0, Berries, Slots(Slot(Berries, 100)));
        f.Eat(Player2, 0, Jam, Slots(Slot(Jam, 150)));
        f.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell, Player1);
        f.Apply(Set(Player2));
        f.Eat(Player1, 0, Berries, Slots(Slot(Berries, 500)));
        var tail = f.Blocks.Sim.Journal.Entries.Where(e => e.Id > checkpoint.BarrierOpId).ToList();
        Assert.True(tail.Count >= 8, $"{tail.Count} operations in the tail");

        var loaded = Load(f.State, checkpoint);
        var replay = new Horyzont.Core.Tests.SimFixture(state: loaded);
        foreach (var entry in tail)
        {
            replay.Sim.Replay(entry);
        }

        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(replay.State));
        Assert.True(replay.State.Progress.TryGet(Player1, out var progress));
        Assert.NotNull(progress.RespawnBed);
        Assert.False(replay.State.Progress.TryGet(Player2, out _));
    }

    private static WorldCheckpoint Complete(FoodWorld f)
    {
        var checkpoint = f.State.CaptureCheckpoint();
        f.State.CompleteCheckpoint(checkpoint);
        return checkpoint;
    }

    /// <summary>A world with two players: player 1 has a bed and two foods, player 2 only bread in the third slot; and the one full checkpoint of it.</summary>
    private static FoodWorld FullWorld(out WorldCheckpoint checkpoint)
    {
        var f = new FoodWorld(players: 2);
        f.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell, Player1);
        f.Apply(Set(Player1, Slot(Berries, 100), Slot(Jam, 200)));
        f.Apply(Set(Player2, default, default, Slot(Bread, 300)));
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

        if (checkpoint.ProgressKeys is { } keys)
        {
            builder.SetProgressKeys(keys);
        }

        foreach (var version in checkpoint.TerrainVersions)
        {
            builder.AddTerrainVersion(version);
        }

        return builder.Build();
    }
}
