using Horyzont.Core.Simulation;

namespace Horyzont.Core.Tests.Simulation;

public sealed class SlidingWindowLimiterTests
{
    [Fact]
    public void CanAccept_SixthEventInWindow_FalseUntilOldestLeaves()
    {
        var limiter = new SlidingWindowLimiter(5, 60);
        for (var i = 0; i < 5; i++)
        {
            Assert.True(limiter.CanAccept(10));
            limiter.Record(10);
        }

        Assert.False(limiter.CanAccept(10));
        Assert.False(limiter.CanAccept(69));
        Assert.True(limiter.CanAccept(70));
    }

    [Fact]
    public void CanAccept_WithoutRecord_DoesNotConsume()
    {
        var limiter = new SlidingWindowLimiter(1, 60);
        limiter.Record(0);

        Assert.False(limiter.CanAccept(59));
        Assert.False(limiter.CanAccept(59));
        Assert.True(limiter.CanAccept(60));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Record_EveryTickAttempts_EveryWindowHoldsAtMostFive(int seed)
    {
        var random = new Random(seed);
        var limiter = new SlidingWindowLimiter(5, 60);
        var accepted = new List<int>();
        for (var tick = 0; tick < 1200; tick++)
        {
            if (random.Next(3) != 0 && limiter.CanAccept((ulong)tick))
            {
                limiter.Record((ulong)tick);
                accepted.Add(tick);
            }
        }

        for (var start = 0; start < 1200 - 59; start++)
        {
            Assert.InRange(accepted.Count(t => t >= start && t < start + 60), 0, 5);
        }

        Assert.True(accepted.Count >= 90);
    }

    [Fact]
    public void RecordAndCanAccept_ManyCalls_DoNotAllocate()
    {
        var limiter = new SlidingWindowLimiter(5, 60);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong tick = 0; tick < 5000; tick++)
        {
            if (limiter.CanAccept(tick))
            {
                limiter.Record(tick);
            }
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Constructor_NonPositiveArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingWindowLimiter(0, 60));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingWindowLimiter(5, 0));
    }
}
