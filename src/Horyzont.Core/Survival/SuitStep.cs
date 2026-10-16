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
    public static SuitStepResult Advance(in SuitResources current, in HazardRates effective, in SuitStats stats, bool regenAllowed, HazardRules rules, int tickRate)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);

        var damage = 0f;
        var mask = 0;
        var heating = One(current.Heating, effective.Cold, stats.Capacity.Heating, stats.Drain.Heating, regenAllowed, HazardKind.Cold, rules, tickRate, ref damage, ref mask);
        var cooling = One(current.Cooling, effective.Heat, stats.Capacity.Cooling, stats.Drain.Cooling, regenAllowed, HazardKind.Heat, rules, tickRate, ref damage, ref mask);
        var filter = One(current.Filter, effective.Toxins, stats.Capacity.Filter, stats.Drain.Filter, regenAllowed, HazardKind.Toxins, rules, tickRate, ref damage, ref mask);
        var shield = One(current.Shield, effective.Radiation, stats.Capacity.Shield, stats.Drain.Shield, regenAllowed, HazardKind.Radiation, rules, tickRate, ref damage, ref mask);
        var oxygen = One(current.Oxygen, effective.Vacuum, stats.Capacity.Oxygen, stats.Drain.Oxygen, regenAllowed, HazardKind.Vacuum, rules, tickRate, ref damage, ref mask);
        return new SuitStepResult(new SuitResources(heating, cooling, filter, shield, oxygen), damage, (byte)mask);
    }

    private static float One(
        float value, float rate, float capacity, float drain, bool regenAllowed, HazardKind kind, HazardRules rules, int tickRate, ref float damage, ref int mask)
    {
        var limit = Usable(capacity);
        var next = MathF.Min(Usable(value), limit);
        var perSecond = Usable(rate);

        next = MathF.Max(0f, next - (perSecond * Usable(drain) / tickRate));
        if (perSecond == 0f && regenAllowed)
        {
            next = MathF.Min(limit, next + (Usable(rules.SuitRegenPerSecond) / tickRate));
        }

        if (perSecond > 0f && next <= 0f)
        {
            damage += rules.ZeroDamage(kind) / tickRate;
            mask |= 1 << ((int)kind - 1);
        }

        return next;
    }

    private static float Usable(float value) => float.IsFinite(value) && value > 0f ? value : 0f;
}
