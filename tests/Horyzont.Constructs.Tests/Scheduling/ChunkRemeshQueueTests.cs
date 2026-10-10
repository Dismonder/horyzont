using Horyzont.Constructs.Scheduling;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Constructs.Tests.Scheduling;

public sealed class ChunkRemeshQueueTests
{
    private static ConstructChunkKey Key(ulong construct, int x = 0, int y = 0, int z = 0) => new(new EntityId(construct), new ChunkPos(x, y, z));

    private static List<RemeshJob> Drain(ChunkRemeshQueue queue)
    {
        List<RemeshJob> jobs = [];
        while (queue.TryStartNext(out var job))
        {
            jobs.Add(job);
            queue.Complete(job);
        }

        return jobs;
    }

    [Fact]
    public void MarkDirty_SameChunkTwice_IsOneJob()
    {
        var queue = new ChunkRemeshQueue(8);

        queue.MarkDirty(Key(1), RemeshPriority.Edit);
        queue.MarkDirty(Key(1), RemeshPriority.Edit);
        queue.MarkDirty(Key(1), RemeshPriority.Rebuild);

        Assert.Equal(1, queue.DirtyCount);
        Assert.Single(Drain(queue));
    }

    [Fact]
    public void InFlight_IsBounded()
    {
        var queue = new ChunkRemeshQueue(2);
        for (var i = 0; i < 5; i++)
        {
            queue.MarkDirty(Key(1, i), RemeshPriority.Rebuild);
        }

        Assert.True(queue.TryStartNext(out var first));
        Assert.True(queue.TryStartNext(out var second));
        Assert.False(queue.TryStartNext(out _));
        Assert.Equal(2, queue.InFlight);
        Assert.Equal(3, queue.DirtyCount);

        Assert.True(queue.Complete(first));
        Assert.True(queue.TryStartNext(out _));
        Assert.False(queue.TryStartNext(out _));
        Assert.True(queue.Complete(second));
    }

    [Fact]
    public void StaleGeneration_IsRejected_AndNewestIsAccepted()
    {
        var queue = new ChunkRemeshQueue(8);
        queue.MarkDirty(Key(1), RemeshPriority.Edit);
        Assert.True(queue.TryStartNext(out var old));

        // An edit arrives while the job runs: the chunk becomes dirty again, the running result is stale.
        queue.MarkDirty(Key(1), RemeshPriority.Edit);

        Assert.False(queue.TryStartNext(out _)); // the same chunk is not started twice at once
        Assert.False(queue.Complete(old));
        Assert.True(queue.TryStartNext(out var current));
        Assert.True(current.Generation > old.Generation);
        Assert.True(queue.Complete(current));
        Assert.Equal(0, queue.Pending);
    }

    [Fact]
    public void RemoveConstruct_ClearsEntries_AndRejectsRunningResults()
    {
        var queue = new ChunkRemeshQueue(8);
        queue.MarkDirty(Key(1, 0), RemeshPriority.Rebuild);
        queue.MarkDirty(Key(1, 1), RemeshPriority.Rebuild);
        queue.MarkDirty(Key(2, 0), RemeshPriority.Rebuild);
        Assert.True(queue.TryStartNext(out var running)); // construct 1, chunk 0
        Assert.Equal(new EntityId(1), running.Key.Construct);

        queue.RemoveConstruct(new EntityId(1));

        Assert.False(queue.Complete(running));
        var rest = Drain(queue);
        Assert.Equal([Key(2, 0)], rest.Select(j => j.Key));
        Assert.Equal(0, queue.Pending);
    }

    [Fact]
    public void RemoveConstruct_ThenMarkAgain_AcceptsOnlyNewGeneration()
    {
        var queue = new ChunkRemeshQueue(8);
        queue.MarkDirty(Key(1), RemeshPriority.Rebuild);
        Assert.True(queue.TryStartNext(out var old));
        queue.RemoveConstruct(new EntityId(1));
        queue.MarkDirty(Key(1), RemeshPriority.Rebuild);

        Assert.False(queue.Complete(old));
        Assert.True(queue.TryStartNext(out var fresh));
        Assert.True(queue.Complete(fresh));
    }

    [Fact]
    public void Edit_IsServedBeforeRebuild_AndUpgradesWaitingRebuild()
    {
        var queue = new ChunkRemeshQueue(8) { Distance = id => id.Value == 1 ? 1.0 : 1000.0 };
        queue.MarkDirty(Key(1, 0), RemeshPriority.Rebuild); // near
        queue.MarkDirty(Key(1, 1), RemeshPriority.Rebuild);
        queue.MarkDirty(Key(2, 0), RemeshPriority.Edit); // far, but an edit
        queue.MarkDirty(Key(1, 1), RemeshPriority.Edit); // upgrade

        var order = Drain(queue).Select(j => (j.Key, j.Priority)).ToArray();

        Assert.Equal(
            [(Key(1, 1), RemeshPriority.Edit), (Key(2, 0), RemeshPriority.Edit), (Key(1, 0), RemeshPriority.Rebuild)], order);
    }

    [Fact]
    public void Ties_AreBrokenByEntityIdThenChunkPos()
    {
        var queue = new ChunkRemeshQueue(16);
        var keys = new[] { Key(3, 0, 0, 1), Key(2, 5, 0, 0), Key(2, 0, 1, 0), Key(2, 0, 0, 2), Key(1, 9, 9, 9), Key(2, 0, 0, 1) };
        foreach (var key in keys)
        {
            queue.MarkDirty(key, RemeshPriority.Rebuild);
        }

        var order = Drain(queue).Select(j => j.Key).ToArray();

        Assert.Equal(keys.Order(ConstructChunkKeyComparer.Instance), order);
        Assert.Equal(Key(1, 9, 9, 9), order[0]);
    }

    [Fact]
    public void Distance_OrdersWithinTheSamePriority()
    {
        var queue = new ChunkRemeshQueue(8) { Distance = id => 100.0 - id.Value };
        queue.MarkDirty(Key(1), RemeshPriority.Rebuild);
        queue.MarkDirty(Key(2), RemeshPriority.Rebuild);
        queue.MarkDirty(Key(3), RemeshPriority.Rebuild);

        Assert.Equal([3UL, 2UL, 1UL], Drain(queue).Select(j => j.Key.Construct.Value));
    }

    [Fact]
    public void Clear_DropsWaitingEntries_AndRunningResults()
    {
        var queue = new ChunkRemeshQueue(8);
        queue.MarkDirty(Key(1, 0), RemeshPriority.Rebuild);
        queue.MarkDirty(Key(1, 1), RemeshPriority.Rebuild);
        Assert.True(queue.TryStartNext(out var running));

        queue.Clear();

        Assert.Equal(0, queue.DirtyCount);
        Assert.False(queue.Complete(running));
        Assert.False(queue.TryStartNext(out _));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveBound() => Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkRemeshQueue(0));

    [Fact]
    public void TryStartNext_ReplicatedLargeConstruct_ReportsSelectionCost()
    {
        // K3-15 observation: a remote client marks every chunk of a 300-chunk construct dirty at once; the pick must stay far below a frame.
        var queue = new ChunkRemeshQueue(8);
        for (var i = 0; i < 300; i++)
        {
            queue.MarkDirty(Key(1, i % 20, i / 20), RemeshPriority.Edit);
        }

        var samples = new List<double>();
        var started = 0;
        while (true)
        {
            var at = System.Diagnostics.Stopwatch.GetTimestamp();
            var ok = queue.TryStartNext(out var job);
            var took = System.Diagnostics.Stopwatch.GetElapsedTime(at).TotalMilliseconds;
            if (!ok)
            {
                break;
            }

            samples.Add(took);
            started++;
            queue.Complete(job);
        }

        Assert.Equal(300, started);

        // The median is the per-frame cost of the pick; the first call (JIT) and a pause of a loaded CI machine hit single samples only,
        // so they cannot fail this check. A pick that scans the whole queue would push the median past 1 ms.
        samples.Sort();
        Assert.InRange(samples[samples.Count / 2], 0, 1);
    }
}
