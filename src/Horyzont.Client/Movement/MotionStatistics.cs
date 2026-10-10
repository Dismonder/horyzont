namespace Horyzont.Client.Movement;

/// <summary>Immutable diagnostics; rendering never formats these counters.</summary>
/// <param name="SmallCorrections">Corrections smoothed over 100 ms.</param>
/// <param name="LargeCorrections">Immediate discontinuities.</param>
/// <param name="MaxPresentationOffset">Largest visual translation in metres.</param>
/// <param name="SampleUnderruns">Remote requests beyond available history.</param>
/// <param name="InterpolationDelaySeconds">Current remote interpolation delay.</param>
public readonly record struct MotionStatistics(long SmallCorrections, long LargeCorrections, float MaxPresentationOffset,
    long SampleUnderruns, double InterpolationDelaySeconds);
