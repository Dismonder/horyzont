namespace Horyzont.Net.Transport.InMemory;

/// <summary>Simulated properties of one in-memory link.</summary>
/// <param name="Latency">Base one-way delay.</param>
/// <param name="Jitter">Maximum extra random delay.</param>
/// <param name="LossRate">Probability of dropping an unreliable message, 0 to 1.</param>
/// <param name="DuplicateRate">Probability of duplicating an unreliable message, 0 to 1.</param>
/// <param name="Seed">Seed of the random generator, so runs are repeatable.</param>
public sealed record LinkConditions(TimeSpan Latency, TimeSpan Jitter, double LossRate, double DuplicateRate, int Seed)
{
    /// <summary>Gets a perfect link with no delay.</summary>
    public static LinkConditions Perfect { get; } = new(TimeSpan.Zero, TimeSpan.Zero, 0, 0, 0);
}
