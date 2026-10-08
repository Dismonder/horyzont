namespace Horyzont.Core.Simulation;

/// <summary>
/// Rate limit counted in integer units and ticks, never in real time: capacity Burst × TickRate units, starts full, every
/// tick adds EditsPerSecond units, an accepted intent costs TickRate units.
/// </summary>
public sealed class TokenBucket
{
    private readonly long _capacity;
    private readonly long _refill;
    private readonly long _cost;
    private long _units;

    /// <summary>Creates a full bucket.</summary>
    /// <param name="editsPerSecond">Refill per second.</param>
    /// <param name="burst">Edits available at once.</param>
    /// <param name="tickRate">Ticks per second.</param>
    public TokenBucket(int editsPerSecond, int burst, int tickRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(editsPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(burst);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);
        _capacity = (long)burst * tickRate;
        _refill = editsPerSecond;
        _cost = tickRate;
        _units = _capacity;
    }

    /// <summary>True when an edit can be paid for.</summary>
    public bool HasToken => _units >= _cost;

    /// <summary>Pays for an accepted edit.</summary>
    public void Consume()
    {
        if (!HasToken)
        {
            throw new InvalidOperationException("The bucket is empty.");
        }

        _units -= _cost;
    }

    /// <summary>Refills one tick.</summary>
    public void Refill() => _units = Math.Min(_capacity, _units + _refill);
}
