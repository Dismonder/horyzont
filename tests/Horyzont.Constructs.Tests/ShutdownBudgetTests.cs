using Horyzont.Game.Constructs;

namespace Horyzont.Constructs.Tests;

public sealed class ShutdownBudgetTests
{
    [Theory]
    [InlineData(-12)]
    [InlineData(12)]
    public void Remaining_WallClockJump_UsesOnlyElapsedTime(int hours)
    {
        var time = new JumpingTimeProvider();
        var budget = new ShutdownBudget(time, TimeSpan.FromSeconds(2));
        time.Advance(TimeSpan.FromMilliseconds(500));
        time.JumpWallClock(TimeSpan.FromHours(hours));
        Assert.Equal(TimeSpan.FromMilliseconds(1500), budget.Remaining);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.Zero, budget.Remaining);
    }
    private sealed class JumpingTimeProvider : TimeProvider
    {
        private DateTimeOffset _utc = DateTimeOffset.UnixEpoch;
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public override DateTimeOffset GetUtcNow() => _utc;

        internal void Advance(TimeSpan elapsed)
        {
            _ticks += elapsed.Ticks;
            _utc += elapsed;
        }

        internal void JumpWallClock(TimeSpan delta) => _utc += delta;
    }
}
