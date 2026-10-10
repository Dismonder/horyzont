namespace Horyzont.Server.Diagnostics;

/// <summary>
/// Measures the duration of server ticks with an injected <see cref="TimeProvider"/> (never <c>Stopwatch</c>) and keeps them in a
/// fixed histogram, so recording allocates nothing. Percentiles have the resolution of <see cref="BucketMicroseconds"/>; mean and
/// maximum are exact. Main thread only.
/// </summary>
public sealed class TickTimeRecorder
{
    /// <summary>Width of one histogram bucket in microseconds.</summary>
    public const long BucketMicroseconds = 50;

    private const int BucketCount = 2001; // 0 .. 100 ms, the last bucket also takes everything longer.

    private readonly TimeProvider _time;
    private readonly int[] _buckets = new int[BucketCount];
    private int _count;
    private long _sumTicks;
    private long _maxTicks;

    /// <summary>Initializes a new instance of the <see cref="TickTimeRecorder"/> class.</summary>
    /// <param name="time">The clock; its timestamps are measured with its own <see cref="TimeProvider.TimestampFrequency"/>.</param>
    public TickTimeRecorder(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>Takes the start timestamp of a tick.</summary>
    /// <returns>The timestamp to give to <see cref="End"/>.</returns>
    public long Begin() => _time.GetTimestamp();

    /// <summary>Records the tick that started at <paramref name="start"/> and ends now.</summary>
    /// <param name="start">The value returned by <see cref="Begin"/>.</param>
    public void End(long start)
    {
        var elapsed = _time.GetTimestamp() - start;
        if (elapsed < 0)
        {
            elapsed = 0;
        }

        var micros = (long)(elapsed * 1_000_000.0 / _time.TimestampFrequency);
        var bucket = Math.Min(micros / BucketMicroseconds, BucketCount - 1);
        _buckets[bucket]++;
        _count++;
        _sumTicks += elapsed;
        if (elapsed > _maxTicks)
        {
            _maxTicks = elapsed;
        }
    }

    /// <summary>Returns the statistics of everything recorded since the previous call and starts a new window.</summary>
    /// <returns>The window; all zeros when nothing was recorded.</returns>
    public TickTimeWindow TakeWindow()
    {
        if (_count == 0)
        {
            return default;
        }

        var toMs = 1000.0 / _time.TimestampFrequency;
        var max = _maxTicks * toMs;
        var window = new TickTimeWindow(
            _count, _sumTicks * toMs / _count, Math.Min(Percentile(0.95, max), max), Math.Min(Percentile(0.99, max), max), max);
        Array.Clear(_buckets);
        _count = 0;
        _sumTicks = 0;
        _maxTicks = 0;
        return window;
    }

    private double Percentile(double fraction, double max)
    {
        var rank = (int)Math.Ceiling(fraction * _count);
        var seen = 0;
        for (var i = 0; i < _buckets.Length; i++)
        {
            seen += _buckets[i];
            if (seen >= rank)
            {
                // The last bucket holds everything longer than the histogram: the exact maximum is the best answer there.
                return i == BucketCount - 1 ? max : (i + 1) * BucketMicroseconds / 1000.0;
            }
        }

        return max;
    }
}
