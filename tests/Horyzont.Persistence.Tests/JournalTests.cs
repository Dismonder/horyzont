using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Persistence.Writing;
using Horyzont.Testing;

namespace Horyzont.Persistence.Tests;

public sealed class JournalTests
{
    [Fact]
    public async Task Batch_FlushIntervalPassed_CommitsOnlyThen()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath);
        var p = h.Join(1);
        h.Leave(p);
        h.Join(1);

        await h.Writer.ProbeAsync();
        Assert.Equal(OpId.None, h.Writer.DurableOpId);
        h.Time.Advance(TimeSpan.FromMilliseconds(99));
        await h.Writer.ProbeAsync();
        Assert.Equal(OpId.None, h.Writer.DurableOpId);
        Assert.Empty(RawDb.JournalIds(dir.WorldPath));

        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        await h.Writer.ProbeAsync();

        Assert.Equal(new OpId(3), h.Writer.DurableOpId);
        Assert.Equal([1L, 2L, 3L], RawDb.JournalIds(dir.WorldPath));
        h.Pump();
        Assert.Equal<PersistenceEvent>([new JournalCommitted(new OpId(3))], h.Events);
    }

    [Fact]
    public async Task Batch_IntervalCountsFromFirstEntryOfBatch()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath);
        var p = h.Join(1);
        h.Time.Advance(TimeSpan.FromMilliseconds(60));
        h.Leave(p);
        await h.Writer.ProbeAsync();
        h.Time.Advance(TimeSpan.FromMilliseconds(40));
        await h.Writer.ProbeAsync();

        Assert.Equal(new OpId(2), h.Writer.DurableOpId);

        h.Join(1);
        h.Time.Advance(TimeSpan.FromMilliseconds(99));
        await h.Writer.ProbeAsync();
        Assert.Equal(new OpId(2), h.Writer.DurableOpId);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        await h.Writer.ProbeAsync();
        Assert.Equal(new OpId(3), h.Writer.DurableOpId);
    }

    [Fact]
    public async Task Batch_MaxBatchEntries_CommitsWithoutTime()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath);
        var p = h.Join(1);
        for (var i = 2; i <= 255; i++)
        {
            if (i % 2 == 0)
            {
                h.Leave(p);
            }
            else
            {
                h.Join(1);
            }
        }

        await h.Writer.ProbeAsync();
        Assert.Equal(OpId.None, h.Writer.DurableOpId);

        h.Leave(p);
        await h.Writer.ProbeAsync();

        Assert.Equal(new OpId(256), h.Writer.DurableOpId);
        Assert.Equal(256, RawDb.JournalIds(dir.WorldPath).Count);
    }

    [Fact]
    public async Task DurableOpId_ScriptRun_GrowsMonotonicallyAndReachesLastOp()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath);
        var script = new WorldScript(7);
        var last = OpId.None;
        var committed = OpId.None;
        for (var i = 0; i < 300; i++)
        {
            script.Step(h.Sim);
            h.Time.Advance(TimeSpan.FromMilliseconds(35));
            var durable = h.Writer.DurableOpId;
            Assert.True(durable >= last, $"DurableOpId went back from {last.Value} to {durable.Value}.");
            last = durable;
            h.Pump();
        }

        await h.FlushAndPump();
        foreach (var evt in h.Events)
        {
            var upTo = Assert.IsType<JournalCommitted>(evt).UpTo;
            Assert.True(upTo > committed);
            committed = upTo;
        }

        Assert.Equal(h.State.LastOpId, h.Writer.DurableOpId);
        Assert.Equal(h.State.LastOpId, committed);
    }

    [Fact]
    public async Task Dispose_PendingBatch_IsCommitted()
    {
        using var dir = new TempDirectory();
        var h = WorldHarness.CreateNew(dir.WorldPath);
        var p = h.Join(1);
        h.Leave(p);
        var hash = StateHasher.Hash(h.State);

        await h.DisposeAsync();

        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        Assert.Equal(2, loaded.JournalTail.Count);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public async Task Load_JournalGap_ThrowsCorruptedWithMissingOpId()
    {
        using var dir = new TempDirectory();
        await RecordOps(dir.WorldPath, 5);
        RawDb.Execute(dir.WorldPath, "DELETE FROM journal WHERE op_id = 3;");

        var error = Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));

        Assert.Equal(new OpId(3), error.OpId);
    }

    [Fact]
    public async Task Load_JournalNotStartingAfterCheckpoint_ThrowsCorrupted()
    {
        using var dir = new TempDirectory();
        await RecordOps(dir.WorldPath, 5);
        RawDb.Execute(dir.WorldPath, "DELETE FROM journal WHERE op_id = 1;");

        var error = Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));

        Assert.Equal(new OpId(1), error.OpId);
    }

    [Fact]
    public async Task Load_CrcColumnChanged_ThrowsCorruptedWithOpId()
    {
        using var dir = new TempDirectory();
        await RecordOps(dir.WorldPath, 5);
        RawDb.Execute(dir.WorldPath, "UPDATE journal SET crc = crc + 1 WHERE op_id = 4;");

        var error = Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));

        Assert.Equal(new OpId(4), error.OpId);
    }

    /// <summary>Alternating join and leave of one player, flushed and closed.</summary>
    internal static async Task RecordOps(string path, int count)
    {
        await using var h = WorldHarness.CreateNew(path);
        var p = h.Join(1);
        for (var i = 2; i <= count; i++)
        {
            if (i % 2 == 0)
            {
                h.Leave(p);
            }
            else
            {
                h.Join(1);
            }
        }

        await h.FlushAndPump();
    }
}
