using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Persistence.Tests;

public sealed class CheckpointTests
{
    [Fact]
    public async Task Checkpoint_JournalUpToBarrierDeleted_LoadedEqualsLiveAndTickOfLastEntry()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath);
        var script = new WorldScript(3);
        for (var i = 0; i < 80; i++)
        {
            script.Step(h.Sim);
        }

        Assert.True(await h.CheckpointAndPump());
        var barrier = h.State.LastOpId;
        var checkpointTick = h.State.CurrentTick;
        Assert.Empty(RawDb.JournalIds(dir.WorldPath));
        Assert.Equal((long)barrier.Value, RawDb.Long(dir.WorldPath, "SELECT value FROM meta WHERE key = 'last_checkpoint_op_id';"));
        Assert.Equal((long)checkpointTick.Value, RawDb.Long(dir.WorldPath, "SELECT value FROM meta WHERE key = 'last_checkpoint_tick';"));
        Assert.Equal((long)barrier.Value, RawDb.Long(dir.WorldPath, "SELECT barrier_op_id FROM checkpoints;"));

        for (var i = 0; i < 40; i++)
        {
            script.Step(h.Sim);
        }

        await h.FlushAndPump();
        var tail = RawDb.JournalIds(dir.WorldPath);
        Assert.NotEmpty(tail);
        Assert.Equal((long)barrier.Value + 1, tail[0]);
        Assert.Equal((long)h.State.LastOpId.Value, tail[^1]);
        var liveHash = StateHasher.Hash(h.State);
        var live = h.State;
        await h.DisposeAsync();

        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.Equal(barrier, loaded.LastCheckpointOpId);
        Assert.Equal(liveHash, StateHasher.Hash(sim.State));
        Assert.Equal(live.LastOpId, sim.State.LastOpId);
        Assert.Equal(loaded.JournalTail[^1].Tick, sim.State.CurrentTick);
    }

    [Fact]
    public async Task Load_EmptyTail_CurrentTickIsCheckpointTick()
    {
        using var dir = new TempDirectory();
        Core.Ids.Tick tick;
        ulong hash;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath))
        {
            h.Join(1);
            h.Ticks(17);
            Assert.True(await h.CheckpointAndPump());
            tick = h.State.CurrentTick;
            hash = StateHasher.Hash(h.State);
        }

        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.Empty(loaded.JournalTail);
        Assert.Equal(new Core.Ids.Tick(17), tick);
        Assert.Equal(tick, sim.State.CurrentTick);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public async Task ConstructRemovedAfterCheckpoint_DoesNotReturn()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath))
        {
            var p = h.Join(1);
            var construct = h.BuildOnTerrain(p, 2);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM entities;"));
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM construct_chunks;"));

            h.State.Constructs.TryGet(construct, out var built);
            h.Remove(p, construct, built!.BoundsMin);
            Assert.False(h.State.Constructs.TryGet(construct, out _));
            Assert.True(await h.CheckpointAndPump());
        }

        Assert.Equal(0L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM entities;"));
        Assert.Equal(0L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM construct_chunks;"));
        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        Assert.Equal(0, sim.State.Constructs.Count);
    }

    [Fact]
    public async Task ConstructRemovedInTail_DoesNotReturn()
    {
        using var dir = new TempDirectory();
        ulong hash;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath))
        {
            var p = h.Join(1);
            var construct = h.BuildOnTerrain(p, 2);
            Assert.True(await h.CheckpointAndPump());
            h.State.Constructs.TryGet(construct, out var built);
            h.Remove(p, construct, built!.BoundsMin);
            hash = StateHasher.Hash(h.State);
        }

        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.Equal(0, sim.State.Constructs.Count);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public async Task OnlinePlayerInCheckpoint_LeaveInTail_LoadsAndReplays()
    {
        using var dir = new TempDirectory();
        PlayerId player;
        ulong hash;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath))
        {
            player = h.Join(1, "alpha");
            h.Dig(player, -2);
            Assert.True(await h.CheckpointAndPump());
            h.Ticks(3);
            h.Leave(player);
            hash = StateHasher.Hash(h.State);
        }

        var loaded = WorldLoader.Load(
            dir.WorldPath, WorldHarness.LoadOptions(dir.WorldPath), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        Assert.True(loaded.State.Players.TryGet(player, out var stored));
        Assert.True(stored.IsOnline);
        Assert.IsType<PlayerLeaveOp>(Assert.Single(loaded.JournalTail).Operation);

        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.True(sim.State.Players.TryGet(player, out var replayed));
        Assert.False(replayed.IsOnline);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public async Task Checkpoint_TerrainBlocks_StoredWithVersionAndData()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath);
        var p = h.Join(1);
        h.Dig(p, -2);

        Assert.True(await h.CheckpointAndPump());

        var versions = h.State.TerrainVersions.All.ToList();
        Assert.NotEmpty(versions);
        var store = new Terrain.TerrainBlockStore(dir.WorldPath);
        foreach (var version in versions)
        {
            Assert.True(store.TryLoad(version.Key, out var stored, out var data));
            Assert.Equal(version.Version, stored);
            Assert.Equal(h.Terrain.GetBlockData(version.Key), data);
        }
    }
}
