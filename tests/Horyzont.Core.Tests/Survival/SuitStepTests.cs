using Horyzont.Core.Survival;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-34, D10 and D16: one tick of the suit. <see cref="ZeroWithHazard_DamagePerSpec"/> is the acceptance test of the table of the spec (docs/spec/03-przetrwanie.md, l. 129 to 135):
/// at zero a resource costs 2, 2, 2, 3 and 10 health per second for cold, heat, toxins, radiation and vacuum.
/// </summary>
public sealed class SuitStepTests
{
    private const int TickRate = 60;

    private static readonly HazardRules _rules = HazardRules.Default;
    private static readonly SuitResources _full = new(100f, 100f, 100f, 100f, 100f);
    private static readonly SuitStats _plain = new(_full, new SuitResources(1f, 1f, 1f, 1f, 1f));

    private static HazardRates Only(HazardKind kind, float rate) => kind switch
    {
        HazardKind.Cold => new HazardRates(rate, 0f, 0f, 0f, 0f),
        HazardKind.Heat => new HazardRates(0f, rate, 0f, 0f, 0f),
        HazardKind.Toxins => new HazardRates(0f, 0f, rate, 0f, 0f),
        HazardKind.Radiation => new HazardRates(0f, 0f, 0f, rate, 0f),
        _ => new HazardRates(0f, 0f, 0f, 0f, rate),
    };

    [Theory]
    [InlineData(HazardKind.Cold, SuitResource.Heating)]
    [InlineData(HazardKind.Heat, SuitResource.Cooling)]
    [InlineData(HazardKind.Toxins, SuitResource.Filter)]
    [InlineData(HazardKind.Radiation, SuitResource.Shield)]
    [InlineData(HazardKind.Vacuum, SuitResource.Oxygen)]
    public void Drain_PerTick(HazardKind kind, SuitResource resource)
    {
        // 6 points per second at 60 ticks per second: 0.1 per tick, from the resource that the hazard drains and from no other.
        var result = SuitStep.Advance(_full, Only(kind, 6f), _plain, false, _rules, TickRate);

        Assert.Equal(99.9f, result.Next[resource], 4);
        foreach (var other in Enum.GetValues<SuitResource>().Where(r => r != resource))
        {
            Assert.Equal(100f, result.Next[other]);
        }

        Assert.Equal(0f, result.Damage);
        Assert.Equal(0, result.ExposedMask);
    }

    [Theory]
    [InlineData(HazardKind.Cold, 2f)]
    [InlineData(HazardKind.Heat, 2f)]
    [InlineData(HazardKind.Toxins, 2f)]
    [InlineData(HazardKind.Radiation, 3f)]
    [InlineData(HazardKind.Vacuum, 10f)]
    public void ZeroWithHazard_DamagePerSpec(HazardKind kind, float damagePerSecond)
    {
        var resource = (SuitResource)(byte)kind;
        var empty = _full.With(resource, 0f);

        // One tick: a sixtieth of the damage per second, and the bit of the resource in the exposed mask.
        var one = SuitStep.Advance(empty, Only(kind, 0.5f), _plain, false, _rules, TickRate);
        Assert.Equal(damagePerSecond / TickRate, one.Damage, 5);
        Assert.Equal(1 << ((byte)kind - 1), one.ExposedMask);
        Assert.Equal(0f, one.Next[resource]);

        // One second: the damage per second of the spec.
        var current = empty;
        var total = 0f;
        for (var i = 0; i < TickRate; i++)
        {
            var step = SuitStep.Advance(current, Only(kind, 0.5f), _plain, false, _rules, TickRate);
            current = step.Next;
            total += step.Damage;
        }

        Assert.Equal(damagePerSecond, total, 2);
    }

    [Fact]
    public void ZeroWithoutHazard_NoDamage()
    {
        // A resource at zero costs nothing while its hazard is absent (or cancelled by protection: the effective rate is 0).
        var empty = new SuitResources(0f, 0f, 0f, 0f, 0f);

        var result = SuitStep.Advance(empty, default, _plain, false, _rules, TickRate);

        Assert.Equal(0f, result.Damage);
        Assert.Equal(0, result.ExposedMask);
        Assert.Equal(empty, result.Next);

        // A hazard of another resource does not hurt through this one: cold is active, the shield is at zero.
        var other = SuitStep.Advance(_full.With(SuitResource.Shield, 0f), Only(HazardKind.Cold, 0.5f), _plain, false, _rules, TickRate);
        Assert.Equal(0f, other.Damage);
        Assert.Equal(0, other.ExposedMask);
    }

    [Fact]
    public void Drain_ReachingZeroInThisTick_HurtsInThisTick()
    {
        // 0.05 left and 6 points per second drain 0.1 in the tick: the resource ends at zero and the damage of this tick counts (step 4 sees the value after the drain).
        var almost = _full.With(SuitResource.Oxygen, 0.05f);

        var result = SuitStep.Advance(almost, Only(HazardKind.Vacuum, 6f), _plain, false, _rules, TickRate);

        Assert.Equal(0f, result.Next.Oxygen);
        Assert.Equal(10f / TickRate, result.Damage, 5);
        Assert.Equal(16, result.ExposedMask);
    }

    [Fact]
    public void Regen_OnlyWhenAllowedAndRateZero()
    {
        var half = new SuitResources(50f, 50f, 50f, 50f, 50f);
        var heatOnly = Only(HazardKind.Heat, 0.5f);

        // Allowed: every resource whose effective rate is 0 regenerates by 5 per second (a twelfth of a point per tick); the cooling drains instead.
        var allowed = SuitStep.Advance(half, heatOnly, _plain, true, _rules, TickRate);
        Assert.Equal(50f + (5f / TickRate), allowed.Next.Heating, 4);
        Assert.Equal(50f - (0.5f / TickRate), allowed.Next.Cooling, 4);
        Assert.Equal(50f + (5f / TickRate), allowed.Next.Filter, 4);
        Assert.Equal(50f + (5f / TickRate), allowed.Next.Shield, 4);
        Assert.Equal(50f + (5f / TickRate), allowed.Next.Oxygen, 4);

        // Not allowed: nothing regenerates, even with no hazard at all (D15: the open ground restores nothing).
        var refused = SuitStep.Advance(half, heatOnly, _plain, false, _rules, TickRate);
        Assert.Equal(50f, refused.Next.Heating);
        Assert.Equal(50f, refused.Next.Oxygen);
        Assert.Equal(half, SuitStep.Advance(half, default, _plain, false, _rules, TickRate).Next);
    }

    [Fact]
    public void Regen_StopsAtCapacity()
    {
        var nearly = new SuitResources(99.99f, 100f, 100f, 100f, 100f);
        var stats = new SuitStats(new SuitResources(120f, 100f, 100f, 100f, 100f), new SuitResources(1f, 1f, 1f, 1f, 1f));

        Assert.Equal(100.0733f, SuitStep.Advance(nearly, default, stats, true, _rules, TickRate).Next.Heating, 3);
        Assert.Equal(120f, SuitStep.Advance(new SuitResources(119.99f, 0f, 0f, 0f, 0f), default, stats, true, _rules, TickRate).Next.Heating, 5);
        Assert.Equal(100f, SuitStep.Advance(_full, default, _plain, true, _rules, TickRate).Next.Oxygen);
    }

    [Fact]
    public void Clamp_ToCapacityFirst()
    {
        // A value above the capacity (a module taken off) is cut to the capacity before the drain, and without hazards simply cut.
        var tooMuch = _full.With(SuitResource.Oxygen, 250f);
        var stats = new SuitStats(_full, new SuitResources(1f, 1f, 1f, 1f, 1f));

        Assert.Equal(100f, SuitStep.Advance(tooMuch, default, stats, false, _rules, TickRate).Next.Oxygen);
        Assert.Equal(99.9f, SuitStep.Advance(tooMuch, Only(HazardKind.Vacuum, 6f), stats, false, _rules, TickRate).Next.Oxygen, 4);
    }

    [Fact]
    public void DrainMultiplier_Applied()
    {
        var halved = new SuitStats(_full, new SuitResources(0.5f, 1f, 1f, 1f, 0.25f));

        var cold = SuitStep.Advance(_full, Only(HazardKind.Cold, 6f), halved, false, _rules, TickRate);
        var vacuum = SuitStep.Advance(_full, Only(HazardKind.Vacuum, 6f), halved, false, _rules, TickRate);

        Assert.Equal(99.95f, cold.Next.Heating, 4);
        Assert.Equal(99.975f, vacuum.Next.Oxygen, 4);
    }

    [Fact]
    public void ExposedMask_Bits()
    {
        // Heating (bit 0) and shield (bit 3) are at zero with their hazards active; the cooling is at zero but heat is not active, the filter is drained but not empty.
        var current = new SuitResources(0f, 0f, 10f, 0f, 100f);
        var effective = new HazardRates(1f, 0f, 1f, 1f, 0f);

        var result = SuitStep.Advance(current, effective, _plain, false, _rules, TickRate);

        Assert.Equal(0b01001, result.ExposedMask);
        Assert.Equal((2f + 3f) / TickRate, result.Damage, 5);
    }

    [Fact]
    public void TickRateNotPositive_Throws()
    {
        Assert.Equal(100f, SuitStep.Advance(_full, default, _plain, false, _rules, 1).Next.Oxygen);

        Assert.Throws<ArgumentOutOfRangeException>(() => SuitStep.Advance(_full, default, _plain, false, _rules, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SuitStep.Advance(_full, default, _plain, false, _rules, -60));
    }

    [Fact]
    public void NonFiniteOrNegativeInput_Ignored()
    {
        // A rate that is not finite or negative counts as 0 (no drain, no damage, no regeneration blocked by it).
        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
        {
            var result = SuitStep.Advance(_full, new HazardRates(bad, bad, bad, bad, bad), _plain, false, _rules, TickRate);

            Assert.Equal(_full, result.Next);
            Assert.Equal(0f, result.Damage);
            Assert.Equal(0, result.ExposedMask);
        }

        // A value that is not finite or negative counts as 0 as well, and no NaN comes out.
        var broken = new SuitResources(float.NaN, -5f, float.PositiveInfinity, 50f, 50f);
        var next = SuitStep.Advance(broken, default, _plain, false, _rules, TickRate);
        Assert.True(next.Next.IsValid);
        Assert.Equal(0f, next.Next.Heating);
        Assert.Equal(0f, next.Next.Cooling);
    }
}
