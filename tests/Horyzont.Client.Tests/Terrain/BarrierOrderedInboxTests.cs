using Horyzont.Client.Terrain;
using Horyzont.Core.Ids;

namespace Horyzont.Client.Tests.Terrain;

public sealed class BarrierOrderedInboxTests
{
    [Fact]
    public void Drain_BarrierBehindQueuedBlocks_IsActedOnOnlyAfterTheyWereApplied()
    {
        var queue = new BarrierOrderedInbox<int>();
        for (var i = 0; i < 6; i++)
        {
            queue.EnqueueBlock(i);
        }

        queue.EnqueueBarrier(new OpId(9), []);
        var log = new List<string>();

        queue.Drain(4, block => { log.Add($"b{block}"); return true; }, (op, _) => log.Add($"barrier{op.Value}"));
        Assert.Equal(["b0", "b1", "b2", "b3"], log);

        queue.Drain(4, block => { log.Add($"b{block}"); return true; }, (op, _) => log.Add($"barrier{op.Value}"));
        Assert.Equal(["b0", "b1", "b2", "b3", "b4", "b5", "barrier9"], log);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Drain_BarrierWithNothingBeforeIt_IsActedOnAtOnce()
    {
        var queue = new BarrierOrderedInbox<int>();
        queue.EnqueueBarrier(new OpId(3), []);
        queue.EnqueueBlock(1);
        var log = new List<string>();

        queue.Drain(1, block => { log.Add($"b{block}"); return true; }, (op, _) => log.Add($"barrier{op.Value}"));

        Assert.Equal(["barrier3", "b1"], log);
    }

    [Fact]
    public void Drain_ApplyReturnsFalse_StopsAndKeepsTheRest()
    {
        var queue = new BarrierOrderedInbox<int>();
        queue.EnqueueBlock(1);
        queue.EnqueueBlock(2);
        queue.EnqueueBarrier(new OpId(1), []);

        queue.Drain(8, _ => false, (_, _) => Assert.Fail("barrier must wait"));

        Assert.Equal(2, queue.Count);
    }
}
