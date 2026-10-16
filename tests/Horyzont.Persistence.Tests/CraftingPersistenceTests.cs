using Horyzont.Core.BlockEntities;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

/// <summary>
/// K6-32 stage C: discoveries (blob 22 version 2), the progress keys (<c>meta.progress_keys</c>, blob 25) and the station states (blob 21 kind 3)
/// go through the checkpoint and come back. No migration: the tables are those of M0003 (D3).
/// </summary>
public sealed class CraftingPersistenceTests
{
    // Loads without the replay of the tail: the simulation replays on the very state it is given, so the state at the checkpoint needs its own load.
    private static LoadedWorld LoadOnly(string path) =>
        WorldLoader.Load(path, WorldHarness.LoadOptions(path), new FakeTimeProvider(), NullLogger.Instance);

    private const string ProgressKeysRow = "SELECT COUNT(*) FROM meta WHERE key = 'progress_keys';";

    [Fact]
    public async Task Checkpoint_WritesProgressKeys_LoadRestores()
    {
        using var dir = new TempDirectory();
        ulong hash;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: CraftingWorldScript.NewWorld()))
        {
            CraftingWorldScript.Build(h);
            h.Sim.ApplyServerOperation(new ProgressKeyGrantOp("test:a"));
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, ProgressKeysRow));

            // The tail: one more key, replayed on top of the checkpoint.
            h.Sim.ApplyServerOperation(new ProgressKeyGrantOp("test:c"));
            await h.FlushAndPump();
            hash = StateHasher.Hash(h.State);
        }

        var loaded = LoadOnly(dir.WorldPath);
        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.Equal(["test:a", "test:boss_2", "test:c"], sim.State.ProgressKeys.Sorted);
        Assert.Equal(["test:a", "test:boss_2"], loaded.State.ProgressKeys.Sorted);
        Assert.Single(loaded.JournalTail);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public async Task Checkpoint_WritesDiscoveries_LoadRestores()
    {
        using var dir = new TempDirectory();
        ulong hash;
        CraftingWorldBuilt built;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: CraftingWorldScript.NewWorld()))
        {
            built = CraftingWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());

            // Alice has the bed and two discoveries, Bob only a discovery: both have a row; the tail adds a discovery to Bob.
            Assert.Equal(2L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM player_progress;"));
            h.Sim.ApplyServerOperation(new ItemDiscoverOp(built.Bob, CraftingWorldScript.Ingot));
            await h.FlushAndPump();
            hash = StateHasher.Hash(h.State);
        }

        var loaded = LoadOnly(dir.WorldPath);
        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.True(loaded.State.Progress.TryGet(built.Alice, out var alice));
        Assert.Equal(CraftingWorldBuilt.Bed(built.Construct), alice.RespawnBed);
        Assert.True(alice.DiscoveredItems.Contains(CraftingWorldScript.Stone) && alice.DiscoveredItems.Contains(CraftingWorldScript.Ore));
        Assert.Equal(2, alice.DiscoveredItems.Count);
        Assert.True(loaded.State.Progress.TryGet(built.Bob, out var bob));
        Assert.Null(bob.RespawnBed);
        Assert.Equal(1, bob.DiscoveredItems.Count);
        Assert.True(sim.State.Progress.TryGet(built.Bob, out var bobAfterReplay));
        Assert.True(bobAfterReplay.DiscoveredItems.Contains(CraftingWorldScript.Ingot));
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public async Task Checkpoint_StationState_LoadRestores()
    {
        using var dir = new TempDirectory();
        ulong hash;
        CraftingWorldBuilt built;
        StationBlockState expected;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: CraftingWorldScript.NewWorld()))
        {
            built = CraftingWorldScript.Build(h);
            Assert.True(h.State.BlockEntities.TryGet(new BlockRef(built.Construct, CraftingWorldBuilt.FurnaceCell), out var live));
            expected = Assert.IsType<StationBlockState>(live);
            Assert.NotNull(expected.Started);
            Assert.True(await h.CheckpointAndPump());

            // The tail: a piece is settled (the compound of a settlement, the state last), replayed on top of the checkpoint.
            h.Sim.ApplyServerOperation(new CompoundOp(
            [
                new ItemDestroyOp(new SlotRef(built.Input, 0), CraftingWorldScript.Ore, 2, ItemSink.Station),
                new ItemDestroyOp(new SlotRef(built.Input, 1), CraftingWorldScript.Peat, 1, ItemSink.Fuel),
                new ItemCreateOp(new SlotRef(built.Output, 0), CraftingWorldScript.Ingot, 1, 5, ItemSource.Station),
                new BlockEntitySetOp(
                    built.Construct, CraftingWorldBuilt.FurnaceCell, CraftingBlocks.FurnaceType, expected with { Started = new Tick(expected.Started.Value.Value + 60) }),
            ]));
            await h.FlushAndPump();
            hash = StateHasher.Hash(h.State);
        }

        var loaded = LoadOnly(dir.WorldPath);
        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.True(loaded.State.BlockEntities.TryGet(new BlockRef(built.Construct, CraftingWorldBuilt.FurnaceCell), out var atCheckpoint));
        Assert.Equal(expected, atCheckpoint);
        Assert.True(loaded.State.Containers.TryGet(built.Input, out var input));
        Assert.Equal((ContainerKind.StationInput, 4), (input.Kind, input.Size));
        Assert.True(loaded.State.Containers.TryGet(built.Output, out var output));
        Assert.Equal(ContainerKind.StationOutput, output.Kind);
        Assert.Equal(new ItemStack(CraftingWorldScript.Ore, 4), input.GetSlot(0));
        Assert.True(sim.State.BlockEntities.TryGet(new BlockRef(built.Construct, CraftingWorldBuilt.FurnaceCell), out var afterReplay));
        Assert.Equal(expected with { Started = new Tick(expected.Started.Value.Value + 60) }, afterReplay);
        Assert.Equal(new ItemStack(CraftingWorldScript.Ingot, 1), sim.State.Containers.TryGet(built.Output, out var replayedOutput) ? replayedOutput.GetSlot(0) : default);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public async Task LoadBlob22Version1_Works()
    {
        using var dir = new TempDirectory();
        FunctionalWorld built;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: FunctionalWorldScript.NewWorld()))
        {
            built = FunctionalWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());
        }

        // A world saved by K6-30 has the version 1 blob of a player with a respawn point only: the bytes of version 3 without discoveries and food, version byte 1.
        dir.ReleasePools();
        var blob = (byte[])RawDb.Scalar(dir.WorldPath, $"SELECT data FROM player_progress WHERE player_id = {built.Player.Value};")!;
        Assert.Equal(new byte[] { 0x16, 0x03 }, blob[..2]);
        Assert.Equal(0, blob[3] & 0x02);
        blob[1] = 0x01;
        RawDb.Execute(dir.WorldPath, $"UPDATE player_progress SET data = $data WHERE player_id = {built.Player.Value};", ("$data", blob));

        var (loaded, _, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.True(loaded.State.Progress.TryGet(built.Player, out var progress));
        Assert.Equal(FunctionalWorld.Bed(built.Construct), progress.RespawnBed);
        Assert.Equal(0, progress.DiscoveredItems.Count);
    }

    [Fact]
    public async Task CorruptProgressKeysBlob_WorldCorrupted()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: CraftingWorldScript.NewWorld()))
        {
            CraftingWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, ProgressKeysRow));
        }

        // The accepted counterpart is the load of the same file above (WorldHarness.Open); the blob now holds a key list that is not sorted.
        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "UPDATE meta SET value = $data WHERE key = 'progress_keys';", ("$data", new byte[] { 0x19, 0x01, 0x02, 0x01, 0x62, 0x01, 0x61 }));

        Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));
    }

    [Fact]
    public async Task NoProgressKeysRow_EmptySet()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: FunctionalWorldScript.NewWorld()))
        {
            FunctionalWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());
        }

        // A world without keys never writes the row (and a world of K6-30 does not have it).
        Assert.Equal(0L, RawDb.Long(dir.WorldPath, ProgressKeysRow));

        var (loaded, _, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.Equal(0, loaded.State.ProgressKeys.Count);
    }

    [Fact]
    public async Task Checkpoint_UnchangedProgressKeys_RowNotRewritten()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: CraftingWorldScript.NewWorld()))
        {
            CraftingWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, ProgressKeysRow));

            // A checkpoint with no key change keeps the row (null in the checkpoint means "unchanged", not "delete").
            h.Sim.ApplyServerOperation(new ItemDiscoverOp(new PlayerId(2), CraftingWorldScript.Ore));
            Assert.True(await h.CheckpointAndPump());
        }

        Assert.Equal(1L, RawDb.Long(dir.WorldPath, ProgressKeysRow));
        var (loaded, _, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        Assert.Equal(["test:boss_2"], loaded.State.ProgressKeys.Sorted);
    }
}
