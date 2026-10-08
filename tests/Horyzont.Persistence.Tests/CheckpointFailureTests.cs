using Horyzont.Core.Hashing;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Persistence.Writing;

namespace Horyzont.Persistence.Tests;

public sealed class CheckpointFailureTests
{
    [Theory]
    [InlineData(FaultPoint.CheckpointMidTransaction)]
    [InlineData(FaultPoint.BeforeCheckpointCommit)]
    public async Task CheckpointError_FaultedState_ThenClearFault_NothingLostNoGaps(FaultPoint point)
    {
        using var dir = new TempDirectory();
        var faults = ScriptedFaults.DiskErrorAt(point);
        ulong hash;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, faults))
        {
            h.FaultsExpected = true;
            var p = h.Join(1);
            var construct = h.BuildOnTerrain(p, 2);
            h.Dig(p, -2);
            Assert.True(await h.Writer.FlushAsync());

            Assert.False(await h.CheckpointAndPump());

            Assert.Contains(h.Events, e => e is CheckpointFailed);
            Assert.IsType<IOException>(Assert.Single(h.Faults));
            Assert.True(h.Writer.IsFaulted);
            Assert.False(h.State.IsCheckpointInFlight);
            Assert.Equal([1L, 2L, 3L], RawDb.JournalIds(dir.WorldPath));
            Assert.Equal(0L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM entities;"));
            Assert.Equal(0L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM checkpoints;"));

            // Faulted: the journal stays in memory, checkpoints fail without touching the database.
            h.Ticks(5);
            h.Dig(p, 1.5f, 1.5f);
            h.BuildOnTerrain(p, -1, 3);
            Assert.False(await h.Writer.FlushAsync());
            var hits = faults.Hits(point);
            Assert.False(await h.CheckpointAndPump());
            Assert.Equal(2, h.CheckpointsFailed);
            Assert.Equal(hits, faults.Hits(point));
            Assert.Equal(0, faults.Hits(FaultPoint.AfterCheckpointCommit));
            Assert.Equal([1L, 2L, 3L], RawDb.JournalIds(dir.WorldPath));
            Assert.Equal(0L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM checkpoints;"));

            h.Writer.ClearFault();
            Assert.True(await h.CheckpointAndPump());

            Assert.False(h.Writer.IsFaulted);
            Assert.Equal(1, h.CheckpointsCommitted);
            Assert.Empty(RawDb.JournalIds(dir.WorldPath));
            Assert.Equal(2L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM entities;"));
            Assert.True(h.State.Constructs.TryGet(construct, out _));

            h.Ticks(2);
            h.Leave(p);
            await h.FlushAndPump();
            Assert.Equal([(long)h.State.LastOpId.Value], RawDb.JournalIds(dir.WorldPath));
            hash = StateHasher.Hash(h.State);
        }

        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        Assert.Single(loaded.JournalTail);
        Assert.Equal(2, sim.State.Constructs.Count);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public async Task ErrorAfterCheckpointCommit_ReportsCommittedThenFaulted()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath, ScriptedFaults.DiskErrorAt(FaultPoint.AfterCheckpointCommit));
        h.FaultsExpected = true;
        h.Join(1);

        Assert.True(await h.CheckpointAndPump());

        Assert.Equal(1, h.CheckpointsCommitted);
        Assert.Single(h.Faults);
        Assert.True(h.Writer.IsFaulted);
        Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM checkpoints;"));

        h.Writer.ClearFault();
        h.Join(2);
        await h.FlushAndPump();
        Assert.Equal([2L], RawDb.JournalIds(dir.WorldPath));
    }
}
