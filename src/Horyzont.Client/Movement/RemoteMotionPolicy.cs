using Horyzont.Core.Ids;

namespace Horyzont.Client.Movement;

/// <summary>Session server clock with a bounded phase slew and one shared interpolation cursor.</summary>
public sealed class RemoteMotionPolicy
{
    /// <summary>Delay at the normal 60 Hz physics rate.</summary>
    public const int InterpolationTicks = 6;
    private readonly TimeProvider _time;
    private long _timestamp;
    private double _tick;
    private double _renderTick;
    private double _rate;
    private double _slewAllowance;
    private Tick _latest;
    private bool _started;

    /// <summary>Creates a clock driven exclusively by the supplied monotonic time provider.</summary>
    /// <param name="time">Session clock.</param>
    public RemoteMotionPolicy(TimeProvider time) { ArgumentNullException.ThrowIfNull(time); _time = time; }

    /// <summary>Gets the monotonic render cursor, 100 ms behind the estimated server time.</summary>
    public double RenderTick
    {
        get { Advance(); return _renderTick; }
    }

    /// <summary>Starts a new clock from ServerHello, without inferring one-way latency from RTT.</summary>
    /// <param name="tick">Hello server tick.</param>
    /// <param name="tickRate">Server physics rate.</param>
    public void Start(Tick tick, double tickRate)
    {
        if (!double.IsFinite(tickRate) || tickRate <= 0) { throw new ArgumentOutOfRangeException(nameof(tickRate)); }
        _rate = tickRate;
        _tick = tick.Value;
        _latest = tick;
        _timestamp = _time.GetTimestamp();
        _renderTick = Math.Max(0, _tick - _rate * 0.1);
        _slewAllowance = 0;
        _started = true;
    }

    /// <summary>Observes a newer snapshot; caller clears all entity buffers when a large phase error resets this clock.</summary>
    /// <param name="tick">Snapshot server tick.</param>
    /// <returns>True only when a phase error exceeding 30 ticks reset the clock.</returns>
    public bool Observe(Tick tick)
    {
        if (!_started || tick <= _latest) { return false; }
        Advance();
        _latest = tick;
        var error = tick.Value - _tick;
        if (Math.Abs(error) > 30) { Start(tick, _rate); return true; }
        var correction = Math.Clamp(error, -_slewAllowance, _slewAllowance);
        _tick += correction;
        _slewAllowance = 0;
        _renderTick = Math.Max(_renderTick, _tick - _rate * 0.1);
        return false;
    }

    /// <summary>Forgets the old session clock.</summary>
    public void Reset() { _started = false; _tick = _renderTick = _slewAllowance = 0; _latest = default; }

    private void Advance()
    {
        if (!_started) { return; }
        var now = _time.GetTimestamp();
        var seconds = _time.GetElapsedTime(_timestamp, now).TotalSeconds;
        _timestamp = now;
        _tick += seconds * _rate;
        _slewAllowance += seconds;
        _renderTick = Math.Max(_renderTick, _tick - _rate * 0.1);
    }
}
