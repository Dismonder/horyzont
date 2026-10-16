using Horyzont.Core.BlockEntities;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Tests.Crafting;
using Horyzont.Core.World;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Checkpoints;

/// <summary>K6-32: progress keys, discoveries and station states travel through the checkpoint, the requeue, the builder and the journal tail.</summary>
public sealed class CraftingCheckpointTests
{
    [Fact]
    public void Capture_ChangedProgressKeys_Included()
    {
        var f = new CraftingFixture();
        var first = Complete(f);
        f.Apply(CraftingFixture.Grant("test:b"));
        f.Apply(CraftingFixture.Grant("test:a"));

        var second = f.State.CaptureCheckpoint();

        Assert.Null(first.ProgressKeys);
        Assert.Equal(["test:a", "test:b"], second.ProgressKeys);
    }

    [Fact]
    public void Capture_UnchangedProgressKeys_Null()
    {
        var f = new CraftingFixture();
        f.Apply(CraftingFixture.Grant("test:a"));
        var first = Complete(f);
        Assert.Equal(["test:a"], first.ProgressKeys);

        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Stone));
        var second = f.State.CaptureCheckpoint();

        // The keys did not change: they are not in the checkpoint, though the world has them.
        Assert.Null(second.ProgressKeys);
        Assert.True(f.State.ProgressKeys.Contains("test:a"));
    }

    [Fact]
    public void Capture_DiscoveryChange_IncludedInProgress()
    {
        var f = new CraftingFixture();
        Complete(f);
        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Stone));

        var second = f.State.CaptureCheckpoint();

        var record = Assert.Single(second.UpsertedProgress);
        Assert.Equal(Player1, record.Player);
        Assert.Null(record.RespawnBed);
        Assert.NotNull(record.Discovered);
        Assert.True(record.Discovered.Contains(CraftingFixture.Stone));
        Assert.Empty(second.RemovedProgress);
    }

    [Fact]
    public void Capture_RespawnClearedWithDiscoveries_UpsertedNotRemoved()
    {
        var f = new CraftingFixture();
        f.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell, Player1);
        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Stone));
        Complete(f);

        // The respawn point is cleared, but the discoveries remain: the record stays and is upserted, it is not removed (D7).
        f.Apply(new RespawnPointSetOp(Player1, null));
        var checkpoint = f.State.CaptureCheckpoint();

        var record = Assert.Single(checkpoint.UpsertedProgress);
        Assert.Equal(Player1, record.Player);
        Assert.Null(record.RespawnBed);
        Assert.True(record.Discovered!.Contains(CraftingFixture.Stone));
        Assert.Empty(checkpoint.RemovedProgress);
    }

    [Fact]
    public void Requeue_MarksProgressKeysAgain()
    {
        var f = new CraftingFixture();
        Complete(f);
        f.Apply(CraftingFixture.Grant("test:a"));
        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Gem));
        var failed = f.State.CaptureCheckpoint();
        f.State.RequeueAfterFailedCheckpoint(failed);

        var retry = f.State.CaptureCheckpoint();

        Assert.Equal(["test:a"], failed.ProgressKeys);
        Assert.Equal(failed.ProgressKeys, retry.ProgressKeys);
        Assert.Single(retry.UpsertedProgress);
        Assert.True(retry.UpsertedProgress[0].Discovered!.Contains(CraftingFixture.Gem));
    }

    [Fact]
    public void Builder_RestoresKeysDiscoveriesAndStation()
    {
        var f = FullWorld(out var checkpoint);

        var loaded = Load(f.State, checkpoint);

        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(loaded));
        Assert.Equal(["test:boss"], loaded.ProgressKeys.Sorted);
        Assert.True(loaded.Progress.TryGet(Player1, out var first));
        Assert.Equal(BedAt(Horyzont.Testing.FunctionalBlocks.BedCell), first.RespawnBed);
        Assert.True(first.DiscoveredItems.Contains(CraftingFixture.Stone));
        Assert.True(loaded.Progress.TryGet(Player2, out var second));
        Assert.Null(second.RespawnBed);
        Assert.True(second.DiscoveredItems.Contains(CraftingFixture.Gem));
        Assert.True(loaded.BlockEntities.TryGet(new BlockRef(ConstructId, CraftingFixture.StationCell), out var station));
        Assert.Equal(f.State.BlockEntities.TryGet(new BlockRef(ConstructId, CraftingFixture.StationCell), out var live) ? live : null, station);
        Assert.Equal(new Tick(100), Assert.IsType<StationBlockState>(station).Started);
    }

    [Fact]
    public void Builder_StationWithoutContainers_Throws()
    {
        var f = FullWorld(out var checkpoint);
        Assert.Equal(2, Load(f.State, checkpoint).Progress.Count);

        var withoutStationContainers = checkpoint.UpsertedContainers.Where(c => c.Kind is not (ContainerKind.StationInput or ContainerKind.StationOutput));
        var withoutOutput = checkpoint.UpsertedContainers.Where(c => c.Kind != ContainerKind.StationOutput);

        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedContainers = [.. withoutStationContainers] }));
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedContainers = [.. withoutOutput] }));
    }

    [Fact]
    public void Builder_StationContainerWithoutState_Throws()
    {
        var f = FullWorld(out var checkpoint);
        Assert.Equal(2, Load(f.State, checkpoint).Progress.Count);

        // The containers of the station stay, its state is dropped (the same rule as for the chest container).
        var withoutState = checkpoint.UpsertedBlockEntities.Where(e => e.State is not StationBlockState);

        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedBlockEntities = [.. withoutState] }));
    }

    [Fact]
    public void Builder_StationStateWithSwappedContainers_Throws()
    {
        var f = FullWorld(out var checkpoint);
        Assert.Equal(2, Load(f.State, checkpoint).Progress.Count);

        var swapped = checkpoint.UpsertedBlockEntities.Select(
            e => e.State is StationBlockState s ? e with { State = s with { Input = s.Output, Output = s.Input } } : e);

        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedBlockEntities = [.. swapped] }));
    }

    [Fact]
    public void Builder_StationContainerOfOtherCell_Throws()
    {
        var f = FullWorld(out var checkpoint, secondStation: true);
        Assert.Equal(2, Load(f.State, checkpoint).Progress.Count);
        var (otherInput, _) = f.StationContainersOf(CraftingFixture.StationCell2);

        // The state of the first station refers to the input container of the second station (hosted by another cell).
        var crossed = checkpoint.UpsertedBlockEntities.Select(
            e => e.Cell == CraftingFixture.StationCell && e.State is StationBlockState s ? e with { State = s with { Input = otherInput } } : e);

        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedBlockEntities = [.. crossed] }));
    }

    [Fact]
    public void Builder_DiscoveryOfUnknownType_Throws()
    {
        var f = FullWorld(out var checkpoint);
        Assert.Equal(2, Load(f.State, checkpoint).Progress.Count);

        var unknown = checkpoint.UpsertedProgress.Select(
            p => p.Player == Player2 ? p with { Discovered = ItemTypeBitSet.Empty.With(new ItemTypeId(99)) } : p);

        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedProgress = [.. unknown] }));
    }

    [Fact]
    public void Builder_EmptyProgressRecord_Throws()
    {
        var f = FullWorld(out var checkpoint);
        Assert.Equal(2, Load(f.State, checkpoint).Progress.Count);

        // A record needs a respawn point or a discovery (D7).
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedProgress = [new PlayerProgressRecord(Player1, null)] }));
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedProgress = [new PlayerProgressRecord(Player1, null, ItemTypeBitSet.Empty)] }));
    }

    [Fact]
    public void Builder_InvalidProgressKeys_Throws()
    {
        var f = FullWorld(out var checkpoint);
        Assert.Equal(["a", "b"], Load(f.State, checkpoint with { ProgressKeys = ["a", "b"] }).ProgressKeys.Sorted);

        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { ProgressKeys = ["b", "a"] }));
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { ProgressKeys = ["a", "a"] }));
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { ProgressKeys = [string.Empty] }));
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { ProgressKeys = [new string('k', 65)] }));
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { ProgressKeys = [.. Enumerable.Range(0, 257).Select(i => "k" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture))] }));
    }

    [Fact]
    public void CheckpointPlusJournal_SameHash()
    {
        var f = new CraftingFixture(players: 2);
        var checkpoint = Complete(f);
        f.Apply(f.PlaceStation(CraftingFixture.StationCell));
        var (input, output) = f.StationContainersOf(CraftingFixture.StationCell);
        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Stone));
        f.Apply(CraftingFixture.Grant("test:boss"));
        f.Apply(new CompoundOp(
        [
            new ItemCreateOp(new SlotRef(input, 0), CraftingFixture.Stone, 4, 500, ItemSource.Admin),
            CraftingFixture.StationState(CraftingFixture.StationCell, input, output, "t:smelt", new Tick(60)),
        ]));
        f.Apply(CraftingFixture.Discover(Player2, CraftingFixture.Gem));
        f.Apply(new CompoundOp(
        [
            new ItemDestroyOp(new SlotRef(input, 0), CraftingFixture.Stone, 2, ItemSink.Station),
            new ItemCreateOp(new SlotRef(output, 0), CraftingFixture.Gem, 1, 5, ItemSource.Station),
            CraftingFixture.StationState(CraftingFixture.StationCell, input, output, "t:smelt", new Tick(120)),
        ]));
        var tail = f.Blocks.Sim.Journal.Entries.Where(e => e.Id > checkpoint.BarrierOpId).ToList();
        Assert.Equal(6, tail.Count);

        var loaded = Load(f.State, checkpoint);
        var replay = new Horyzont.Core.Tests.SimFixture(state: loaded);
        foreach (var entry in tail)
        {
            replay.Sim.Replay(entry);
        }

        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(replay.State));
        Assert.True(replay.State.ProgressKeys.Contains("test:boss"));
        Assert.Equal(2, replay.State.Progress.Count);
    }

    private static WorldCheckpoint Complete(CraftingFixture f)
    {
        var checkpoint = f.State.CaptureCheckpoint();
        f.State.CompleteCheckpoint(checkpoint);
        return checkpoint;
    }

    /// <summary>A world with a chest, an owned bed with its respawn point, a station in work, two players with discoveries and a progress key, and the one full checkpoint of it.</summary>
    private static CraftingFixture FullWorld(out WorldCheckpoint checkpoint, bool secondStation = false)
    {
        var f = new CraftingFixture(players: 2);
        f.Blocks.SetUpChest(Horyzont.Testing.FunctionalBlocks.ChestCell);
        f.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell, Player1);
        f.SetUpStation(CraftingFixture.StationCell, "t:a", new Tick(100));
        if (secondStation)
        {
            f.SetUpStation(CraftingFixture.StationCell2);
        }

        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Stone));
        f.Apply(CraftingFixture.Discover(Player2, CraftingFixture.Gem));
        f.Apply(CraftingFixture.Grant("test:boss"));
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
