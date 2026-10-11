using Horyzont.Core.Ids;
using Horyzont.Server.Replication;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests;

public sealed class ReplicationBudgetTests
{
    [Fact]
    public void FixedBudget_Debit_DoesNotChangeK2Limit()
    {
        var budget = new FixedSnapshotBudget();
        budget.Debit(new PlayerId(1), 1150);
        Assert.Equal(1150, budget.GetByteBudget(new PlayerId(1), new Tick(42)));
    }

    [Fact]
    public void TryConsume_SharedBulkAndLive_RefillsAt27000AndKeepsDebt()
    {
        var time = new FakeTimeProvider();
        var budget = new TokenBucketReplicationBudget(time);
        var player = new PlayerId(1);
        Assert.True(budget.TryConsume(player, ReplicationTraffic.Bulk, 65536));
        Assert.False(budget.TryConsume(player, ReplicationTraffic.Bulk, 1));
        Assert.True(budget.TryConsume(player, ReplicationTraffic.Live, 27000));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(budget.TryConsume(player, ReplicationTraffic.Bulk, 1));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(budget.TryConsume(player, ReplicationTraffic.Bulk, 27000));
        Assert.False(budget.TryConsume(player, ReplicationTraffic.Bulk, 1));
        Assert.True(budget.TryConsume(new PlayerId(2), ReplicationTraffic.Bulk, 65536));
    }

    [Fact]
    public void Forget_AfterDebt_RestoresFullBucketAndDropsState()
    {
        var time = new FakeTimeProvider();
        var budget = new TokenBucketReplicationBudget(time);
        var player = new PlayerId(1);
        Assert.True(budget.TryConsume(player, ReplicationTraffic.Bulk, 65536));
        Assert.False(budget.TryConsume(player, ReplicationTraffic.Bulk, 1));
        budget.Forget(player);
        Assert.True(budget.TryConsume(player, ReplicationTraffic.Bulk, 65536));
        budget.Forget(new PlayerId(99));
    }

    [Fact]
    public void TryConsume_LargeBulk_RequiresCapacityAndDebitsActualBytes()
    {
        var time = new FakeTimeProvider();
        var budget = new TokenBucketReplicationBudget(time);
        var player = new PlayerId(1);
        Assert.True(budget.TryConsume(player, ReplicationTraffic.Bulk, 131072));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(budget.TryConsume(player, ReplicationTraffic.Bulk, 1));
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(budget.TryConsume(player, ReplicationTraffic.Bulk, 65536));
        Assert.False(budget.TryConsume(player, ReplicationTraffic.Bulk, 1));
    }
    [Fact]
    public void TryConsume_WarmedPlayer_DoesNotAllocate()
    {
        var budget = new TokenBucketReplicationBudget(new FakeTimeProvider());
        var player = new PlayerId(1);
        for (var i = 0; i < 100; i++)
        {
            budget.TryConsume(player, ReplicationTraffic.Live, 1);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            budget.TryConsume(player, ReplicationTraffic.Live, 1);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

}
