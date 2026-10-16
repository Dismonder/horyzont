namespace Horyzont.Core.Survival;

/// <summary>One tick of the suit as a pure function (K6-34, D10, D16); allocates nothing.</summary>
public static class SuitStep
{
    /// <summary>
    /// Advances the suit by one tick, in this order: (1) every value is clamped to its capacity; (2) every resource drains by
    /// <c>effective[r] * Drain[r] / tickRate</c>, to at least 0; (3) a resource whose effective rate is 0 regenerates by
    /// <see cref="HazardRules.SuitRegenPerSecond"/> / <paramref name="tickRate"/>, to at most the capacity, when <paramref name="regenAllowed"/>; (4) a resource at zero after the
    /// drain with an effective rate above 0 adds <see cref="HazardRules.ZeroDamage"/> / <paramref name="tickRate"/> to the damage and its bit to the exposed mask.
    /// </summary>
    /// <param name="current">The resources before the tick.</param>
    /// <param name="effective">The rates after protection, per second.</param>
    /// <param name="stats">The capacities and drain multipliers.</param>
    /// <param name="regenAllowed">Whether regeneration is allowed (shelter, generator or life support).</param>
    /// <param name="rules">The rules.</param>
    /// <param name="tickRate">Ticks per second.</param>
    /// <returns>The new resources, the damage and the exposed mask.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRate"/> is not positive.</exception>
    /// <remarks>A non-finite or negative value of an input counts as 0.</remarks>
    public static SuitStepResult Advance(in SuitResources current, in HazardRates effective, in SuitStats stats, bool regenAllowed, HazardRules rules, int tickRate) =>
        throw new NotImplementedException("K6-34 A6");
}
