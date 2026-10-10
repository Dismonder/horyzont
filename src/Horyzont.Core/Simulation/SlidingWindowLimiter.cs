namespace Horyzont.Core.Simulation;

/// <summary>
/// Sliding window rate limit counted in ticks (K2-13 W3, gate decision D13): at most <c>maxEvents</c> accepted events in every
/// window of <c>windowTicks</c> consecutive ticks. Keeps the ticks of the last accepted events in a ring, so it never allocates
/// after construction. A rejected event costs nothing: only <see cref="Record"/> consumes the window.
/// </summary>
public sealed class SlidingWindowLimiter
{
    private readonly ulong[] _ticks;
    private readonly ulong _windowTicks;
    private int _next;
    private int _count;

    /// <summary>Creates an empty limiter.</summary>
    /// <param name="maxEvents">Accepted events allowed in one window.</param>
    /// <param name="windowTicks">Window length in ticks (the tick rate for a one second window).</param>
    public SlidingWindowLimiter(int maxEvents, int windowTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEvents);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowTicks);
        _ticks = new ulong[maxEvents];
        _windowTicks = (ulong)windowTicks;
    }

    /// <summary>
    /// True when fewer than <c>maxEvents</c> events were recorded in the ticks <c>(tick - windowTicks, tick]</c>.
    /// </summary>
    /// <param name="tick">Current tick; not before the ticks of the recorded events.</param>
    /// <returns>True when one more event fits in the window.</returns>
    public bool CanAccept(ulong tick)
    {
        if (_count < _ticks.Length)
        {
            return true;
        }

        // The ring is full: _next points at the oldest of the last maxEvents events. It leaves the window at oldest + windowTicks.
        return _ticks[_next] + _windowTicks <= tick;
    }

    /// <summary>Records an accepted event at a tick. The caller checked <see cref="CanAccept"/> first.</summary>
    /// <param name="tick">Current tick.</param>
    public void Record(ulong tick)
    {
        _ticks[_next] = tick;
        _next = (_next + 1) % _ticks.Length;
        if (_count < _ticks.Length)
        {
            _count++;
        }
    }
}
