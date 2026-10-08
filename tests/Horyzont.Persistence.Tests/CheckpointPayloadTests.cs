using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Persistence.Writing;

namespace Horyzont.Persistence.Tests;

/// <summary>Terrain payloads of a checkpoint must be exactly the block versions covered by its barrier.</summary>
public sealed class CheckpointPayloadTests
{
    [Fact]
    public async Task ExtraStalePayload_IsRejected_SavedVersionDoesNotRegress()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath);
        var p = h.Join(1);
        h.Dig(p, -2);
        var stale = h.State.TerrainVersions.All.ToDictionary(v => v.Key, v => (v.Version, Data: h.Terrain.GetBlockData(v.Key)));
        h.Dig(p, -2);
        Assert.True(await h.CheckpointAndPump());
        var key = stale.Keys.First(k => h.State.TerrainVersions.Get(k) > stale[k].Version);
        var saved = h.State.TerrainVersions.Get(key);

        var core = h.State.CaptureCheckpoint();
        Assert.DoesNotContain(core.TerrainVersions, v => v.Key == key);
        var payloads = Payloads(h, core);
        payloads.Add(new TerrainBlockPayload(key, stale[key].Version, stale[key].Data));

        Assert.Throws<ArgumentException>(() => { _ = h.Writer.CheckpointAsync(new CheckpointRequest(core, payloads)); });
        h.State.RequeueAfterFailedCheckpoint(core);
        await h.Writer.ProbeAsync();

        Assert.True(new TerrainBlockStore(dir.WorldPath).TryLoad(key, out var version, out var data));
        Assert.Equal(saved, version);
        Assert.Equal(h.Terrain.GetBlockData(key), data);
    }

    [Fact]
    public async Task ExtraPayloadBeyondBarrier_IsRejected_ReplayDoesNotApplyTwice()
    {
        using var dir = new TempDirectory();
        ulong liveHash;
        HashSet<TerrainBlockVersion> live;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath))
        {
            var p = h.Join(1);
            await h.FlushAndPump();
            var core = h.State.CaptureCheckpoint();
            var payloads = Payloads(h, core);
            var captured = core.TerrainVersions.Select(v => v.Key).ToHashSet();

            h.Dig(p, -2);
            var beyond = h.State.TerrainVersions.All.Where(v => !captured.Contains(v.Key)).ToList();
            Assert.NotEmpty(beyond);
            payloads.AddRange(beyond.Select(v => new TerrainBlockPayload(v.Key, v.Version, h.Terrain.GetBlockData(v.Key))));

            Assert.Throws<ArgumentException>(() => { _ = h.Writer.CheckpointAsync(new CheckpointRequest(core, payloads)); });
            h.State.RequeueAfterFailedCheckpoint(core);
            Assert.True(await h.CheckpointAndPump());
            liveHash = StateHasher.Hash(h.State);
            live = [.. h.State.TerrainVersions.All];
        }

        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        Assert.Equal(liveHash, StateHasher.Hash(sim.State));
        Assert.True(live.SetEquals(sim.State.TerrainVersions.All));
    }

    [Fact]
    public async Task PayloadWithOtherVersionThanCaptured_IsRejected()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath);
        var p = h.Join(1);
        h.Dig(p, -2);
        await h.FlushAndPump();
        var core = h.State.CaptureCheckpoint();
        var first = core.TerrainVersions[0];
        var payloads = Payloads(h, core);
        payloads[0] = new TerrainBlockPayload(first.Key, first.Version + 1, payloads[0].Data);

        Assert.Throws<ArgumentException>(() => { _ = h.Writer.CheckpointAsync(new CheckpointRequest(core, payloads)); });
        h.State.RequeueAfterFailedCheckpoint(core);
    }

    private static List<TerrainBlockPayload> Payloads(WorldHarness h, WorldCheckpoint core) =>
        [.. core.TerrainVersions.Select(v => new TerrainBlockPayload(v.Key, v.Version, h.Terrain.GetBlockData(v.Key)))];
}
