using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Persistence.Writing;

namespace Horyzont.Persistence.Tests;

public sealed class JournalFailureTests
{
    [Fact]
    public async Task BatchError_LaterEntriesNotOnDisk_ClearFaultWritesThemInOrder()
    {
        using var dir = new TempDirectory();
        var faults = ScriptedFaults.DiskErrorAt(FaultPoint.BeforeJournalCommit, hit: 2);
        ulong hash;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, faults))
        {
            h.FaultsExpected = true;
            var p = h.Join(1);
            h.Dig(p, -2);
            Assert.True(await h.Writer.FlushAsync());

            h.Ticks(10);
            h.Dig(p, 2);
            h.Leave(p);
            Assert.False(await h.Writer.FlushAsync());
            h.Pump();

            Assert.True(h.Writer.IsFaulted);
            Assert.IsType<IOException>(Assert.Single(h.Faults));
            Assert.Equal(new OpId(2), h.Writer.DurableOpId);
            Assert.Equal([1L, 2L], RawDb.JournalIds(dir.WorldPath));

            h.Join(1);
            h.Join(2);
            h.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.False(await h.Writer.FlushAsync());
            Assert.Equal([1L, 2L], RawDb.JournalIds(dir.WorldPath));

            h.Writer.ClearFault();
            Assert.True(await h.Writer.FlushAsync());
            h.Pump();

            Assert.False(h.Writer.IsFaulted);
            Assert.Equal(new OpId(6), h.Writer.DurableOpId);
            Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L], RawDb.JournalIds(dir.WorldPath));
            Assert.Equal(new JournalCommitted(new OpId(6)), h.Events[^1]);
            hash = StateHasher.Hash(h.State);
        }

        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        Assert.Equal(6, loaded.JournalTail.Count);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    /// <summary>The commit reached the disk but reported an error: the entries stay pending and are not acknowledged.</summary>
    [Fact]
    public async Task AmbiguousJournalCommit_ClearFaultSkipsEntriesOnDisk_NoDuplicateNoGap()
    {
        using var dir = new TempDirectory();
        ulong hash;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, ScriptedFaults.DiskErrorAt(FaultPoint.AfterJournalCommit)))
        {
            h.FaultsExpected = true;
            var p = h.Join(1);
            h.Leave(p);

            Assert.False(await h.Writer.FlushAsync());
            h.Pump();
            Assert.True(h.Writer.IsFaulted);
            Assert.Equal(OpId.None, h.Writer.DurableOpId);
            Assert.DoesNotContain(h.Events, e => e is JournalCommitted);
            Assert.Equal([1L, 2L], RawDb.JournalIds(dir.WorldPath));

            h.Join(1);
            h.Writer.ClearFault();
            Assert.True(await h.Writer.FlushAsync());
            h.Pump();

            Assert.False(h.Writer.IsFaulted);
            Assert.Equal(new OpId(3), h.Writer.DurableOpId);
            Assert.Equal([1L, 2L, 3L], RawDb.JournalIds(dir.WorldPath));
            Assert.Equal(
                [new JournalCommitted(new OpId(2)), new JournalCommitted(new OpId(3))],
                h.Events.OfType<JournalCommitted>().ToList());
            hash = StateHasher.Hash(h.State);
        }

        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        Assert.Equal(3, loaded.JournalTail.Count);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public async Task AmbiguousJournalCommit_NothingNewPending_ClearFaultReportsDiskProgress()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath, ScriptedFaults.DiskErrorAt(FaultPoint.AfterJournalCommit));
        h.FaultsExpected = true;
        h.Join(1);
        Assert.False(await h.Writer.FlushAsync());

        h.Writer.ClearFault();
        Assert.True(await h.Writer.FlushAsync());
        h.Pump();

        Assert.False(h.Writer.IsFaulted);
        Assert.Equal(new OpId(1), h.Writer.DurableOpId);
        Assert.Equal(new JournalCommitted(new OpId(1)), Assert.Single(h.Events.OfType<JournalCommitted>()));
        Assert.Equal([1L], RawDb.JournalIds(dir.WorldPath));
    }

    [Fact]
    public async Task EntryOutOfOrder_IsRefusedAndFaults()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath);
        h.FaultsExpected = true;
        var p = h.Join(1);
        await h.FlushAndPump();
        var leave = new PlayerLeaveOp(p, default, 0, default, System.Numerics.Quaternion.Identity, 0);

        h.Writer.EnqueueJournal(new JournalEntry(new OpId(1), default, null, leave));
        h.Writer.EnqueueJournal(new JournalEntry(new OpId(3), default, null, leave));

        Assert.False(await h.Writer.FlushAsync());
        h.Pump();
        Assert.Equal(2, h.Faults.Count);
        Assert.Equal([1L], RawDb.JournalIds(dir.WorldPath));
    }
}
