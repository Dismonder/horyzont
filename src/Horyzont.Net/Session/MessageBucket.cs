namespace Horyzont.Net.Session;

/// <summary>
/// Token bucket of messages per second with a burst of two seconds, refilled lazily from the monotonic session clock. Units are
/// messages × <see cref="TimeSpan.TicksPerSecond"/>, so refills are exact integers.
/// </summary>
internal sealed class MessageBucket
{
    private const long BurstSeconds = 2;

    private readonly long _rate;
    private readonly long _capacity;
    private long _units;
    private long _lastRefill;

    public MessageBucket(int messagesPerSecond, long nowTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(messagesPerSecond);
        _rate = messagesPerSecond;
        _capacity = _rate * BurstSeconds * TimeSpan.TicksPerSecond;
        _units = _capacity;
        _lastRefill = nowTicks;
    }

    /// <summary>Takes one message token.</summary>
    /// <param name="nowTicks">Current monotonic time in ticks; it never decreases.</param>
    /// <returns>False when the bucket is empty; the message must be dropped.</returns>
    public bool TryTake(long nowTicks)
    {
        var elapsed = nowTicks - _lastRefill;
        if (elapsed > 0)
        {
            _units = elapsed >= BurstSeconds * TimeSpan.TicksPerSecond ? _capacity : Math.Min(_capacity, _units + (elapsed * _rate));
            _lastRefill = nowTicks;
        }

        if (_units < TimeSpan.TicksPerSecond)
        {
            return false;
        }

        _units -= TimeSpan.TicksPerSecond;
        return true;
    }
}
