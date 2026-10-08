namespace Horyzont.Net.Session;

/// <summary>
/// Smoothed round-trip time: exponential average with α = 1/8 and mean deviation with β = 1/4, as in RFC 6298.
/// The first sample sets the average and half of it as the deviation.
/// </summary>
public sealed class RttEstimator
{
    private const double Alpha = 0.125;
    private const double Beta = 0.25;

    private double _smoothedTicks;
    private double _deviationTicks;

    /// <summary>Gets a value indicating whether at least one sample arrived.</summary>
    public bool HasSamples { get; private set; }

    /// <summary>Gets the smoothed round-trip time, or zero before the first sample.</summary>
    public TimeSpan Smoothed => TimeSpan.FromTicks((long)Math.Round(_smoothedTicks));

    /// <summary>Gets the mean deviation of the round-trip time.</summary>
    public TimeSpan Deviation => TimeSpan.FromTicks((long)Math.Round(_deviationTicks));

    /// <summary>Adds one measured round trip; negative samples are ignored.</summary>
    /// <param name="sample">The measured round-trip time.</param>
    public void AddSample(TimeSpan sample)
    {
        if (sample < TimeSpan.Zero)
        {
            return;
        }

        double ticks = sample.Ticks;
        if (!HasSamples)
        {
            _smoothedTicks = ticks;
            _deviationTicks = ticks / 2;
            HasSamples = true;
            return;
        }

        _deviationTicks = ((1 - Beta) * _deviationTicks) + (Beta * Math.Abs(_smoothedTicks - ticks));
        _smoothedTicks = ((1 - Alpha) * _smoothedTicks) + (Alpha * ticks);
    }
}
