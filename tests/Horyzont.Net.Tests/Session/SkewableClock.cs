using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Net.Tests.Session;

/// <summary>
/// A clock whose wall time can jump in both directions independently of elapsed time: the monotonic timestamp follows
/// the inner <see cref="FakeTimeProvider"/> (which only moves forward), the UTC time adds <see cref="UtcOffset"/>.
/// </summary>
public sealed class SkewableClock(FakeTimeProvider inner) : TimeProvider
{
    public TimeSpan UtcOffset { get; set; }

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow() + UtcOffset;

    public override long GetTimestamp() => inner.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        inner.CreateTimer(callback, state, dueTime, period);
}
