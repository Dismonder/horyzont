namespace Horyzont.Net.Transport.InMemory;

/// <summary>Directional state of one simulated link between two in-memory transports.</summary>
internal sealed class InMemoryLink
{
    private Random _random;

    public InMemoryLink(LinkConditions conditions)
    {
        Conditions = conditions;
        _random = new Random(conditions.Seed);
    }

    public LinkConditions Conditions { get; private set; }

    public bool Partitioned { get; set; }

    public void Apply(LinkConditions conditions)
    {
        Conditions = conditions;
        _random = new Random(conditions.Seed);
    }

    public double NextDouble() => _random.NextDouble();

    /// <summary>Returns a one-way delay: base latency plus a random share of the jitter.</summary>
    public TimeSpan NextDelay() => Conditions.Latency + (Conditions.Jitter * _random.NextDouble());
}
