namespace Horyzont.Client.Diagnostics;

/// <summary>Fixed render ring and transport counter deltas, sampled outside the simulation.</summary>
public sealed class HudSampler
{
    private readonly IHudMetricsSource _source;
    private readonly TimeProvider _time;
    private readonly double[] _deltas = new double[60];
    private int _next;
    private int _count;
    private double _total;
    private int _generation;
    private bool _hasCounters;
    private long _sent;
    private long _received;
    private long _at;

    /// <summary>Creates a sampler with an injected monotonic clock.</summary>
    public HudSampler(IHudMetricsSource source, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(time);
        _source = source;
        _time = time;
        _generation = source.SessionGeneration;
    }

    /// <summary>Records a positive finite render delta; ignores paused or invalid frames.</summary>
    public void NoteRenderDelta(double delta)
    {
        if (!double.IsFinite(delta) || delta <= 0)
        {
            return;
        }
        _total -= _deltas[_next];
        _deltas[_next] = delta;
        _total += delta;
        _next = (_next + 1) % _deltas.Length;
        _count = Math.Min(_count + 1, _deltas.Length);
    }

    /// <summary>Reads metrics without string formatting or allocation.</summary>
    public HudSample Sample()
    {
        HudFields present = HudFields.ReadOnly;
        double fps = _total > 0 ? _count / _total : 0;
        if (_count > 0)
        {
            present |= HudFields.Fps;
        }
        if (_source.TryGetServerTick(out ulong tick))
        {
            present |= HudFields.ServerTick;
        }
        if (_source.TryGetServerTickMilliseconds(out double tickMs))
        {
            present |= HudFields.ServerTickMilliseconds;
        }
        if (_source.TryGetRtt(out double rtt))
        {
            present |= HudFields.Rtt;
        }
        if (_source.TryGetTools(out int pending, out int rejections))
        {
            present |= HudFields.Tools;
        }
        if (_generation != _source.SessionGeneration)
        {
            _generation = _source.SessionGeneration;
            _hasCounters = false;
            Array.Clear(_deltas);
            _next = _count = 0;
            _total = 0;
            present &= ~HudFields.Fps;
            fps = 0;
        }
        double sentRate = 0;
        double receivedRate = 0;
        long at = _time.GetTimestamp();
        if (_source.TryGetTransportBytes(out long sent, out long received))
        {
            double elapsed = _hasCounters ? _time.GetElapsedTime(_at, at).TotalSeconds : 0;
            if (elapsed > 0 && sent >= _sent && received >= _received)
            {
                sentRate = (sent - _sent) / elapsed;
                receivedRate = (received - _received) / elapsed;
                present |= HudFields.Bandwidth;
            }
            _sent = sent;
            _received = received;
            _at = at;
            _hasCounters = true;
        }
        else
        {
            _hasCounters = false;
        }
        return new(present, fps, tickMs, tick, rtt, sentRate, receivedRate, pending, rejections, _source.IsReadOnly);
    }
}
