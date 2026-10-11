using Horyzont.Core.Ids;
using Horyzont.Server.Replication;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests.Replication;

public sealed class MotionSnapshotBudgetTests
{
    [Fact]
    public void Debit_ActualBytesAndTime_EnforcesRateAndFrameLimits()
    {
        var time = new FakeTimeProvider();
        var budget = new MotionSnapshotBudget(time);
        var player = new PlayerId(1);
        Assert.Equal(1024, budget.GetByteBudget(player, new Tick(1)));
        Assert.Equal(1150, budget.GetByteBudget(player, new Tick(1), true));
        budget.Debit(player, 1150);
        budget.Debit(player, 1150);
        Assert.Equal(0, budget.GetByteBudget(player, new Tick(1)));
        Assert.Equal(1024, budget.GetByteBudget(new PlayerId(2), new Tick(1)));
        time.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(1024, budget.GetByteBudget(player, new Tick(2), true));
        budget.Debit(player, 400);
        Assert.Equal(624, budget.GetByteBudget(player, new Tick(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Debit(player, 1151));
        Assert.Throws<InvalidOperationException>(() => budget.Debit(player, 625));
        time.Advance(TimeSpan.FromSeconds(10));
        budget.Debit(player, 1150);
        budget.Debit(player, 1150);
        Assert.Equal(0, budget.GetByteBudget(player, new Tick(3)));
        budget.Remove(player);
        Assert.Equal(1024, budget.GetByteBudget(player, new Tick(1)));
    }

    [Fact]
    public void GetByteBudget_TwentyHzSteadyState_StaysWithin20480BytesPerSecond()
    {
        var time = new FakeTimeProvider();
        var budget = new MotionSnapshotBudget(time);
        var player = new PlayerId(1);
        budget.Debit(player, 1150);
        budget.Debit(player, 1150);
        var total = 0;
        for (ulong tick = 1; tick <= 20; tick++)
        {
            time.Advance(TimeSpan.FromMilliseconds(50));
            var bytes = budget.GetByteBudget(player, new Tick(tick * 3));
            total += bytes;
            budget.Debit(player, bytes);
        }
        Assert.Equal(20480, total);
    }

    [Fact]
    public void GetAndDebit_WarmedUp_AllocatesZeroBytes()
    {
        var budget = new MotionSnapshotBudget(new FakeTimeProvider());
        var player = new PlayerId(1);
        for (var i = 0; i < 1000; i++) { budget.GetByteBudget(player, default); budget.Debit(player, 0); }
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) { budget.GetByteBudget(player, default); budget.Debit(player, 0); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
    }
}
