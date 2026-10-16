using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Survival;
using Horyzont.Core.Tests.Serialization;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-34, D10: whatever the hazards, the protection and the modules, the suit stays inside 0 and its capacity, the damage is finite and not negative, the exposed mask agrees with the
/// resources, and a protected player without hazards is refilled to the capacity (profile of the cases as in <see cref="VitalsStepProperties"/>).
/// </summary>
public sealed class SuitStepProperties
{
    private const int TickRate = 60;
    private const int SequenceCases = 500;
    private const int RefillCases = 100;
    private const int StepsPerSequence = 200;
    private const int CoverageCases = 100;

    /// <summary>The largest capacity is 300 and the regeneration 5 per second: 300 / 5 * 60 = 3600 ticks, with a margin.</summary>
    private const int RefillTicks = 4000;

    private static readonly HazardRules _rules = HazardRules.Default;

    private static Gen<float> RateGen { get; } = Gen.Elements(0f, 0f, 0f, 0.25f, 1f, 6f, 40f);

    private static Gen<HazardRates> RatesGen { get; } =
        from cold in RateGen
        from heat in RateGen
        from toxins in RateGen
        from radiation in RateGen
        from vacuum in RateGen
        select new HazardRates(cold, heat, toxins, radiation, vacuum);

    private static Gen<float> CapacityGen { get; } = Gen.Choose(50, 300).Select(c => (float)c);

    private static Gen<float> DrainGen { get; } = Gen.Elements(0.25f, 0.5f, 1f);

    private static Gen<float> ValueGen { get; } = Gen.Choose(0, 300).Select(v => (float)v);

    private static Gen<SuitResources> ResourcesGen(Gen<float> gen) =>
        from a in gen
        from b in gen
        from c in gen
        from d in gen
        from e in gen
        select new SuitResources(a, b, c, d, e);

    private static Gen<SuitStats> StatsGen { get; } =
        from capacity in ResourcesGen(CapacityGen)
        from drain in ResourcesGen(DrainGen)
        select new SuitStats(capacity, drain);

    /// <summary>A whole case has one profile of hazards: calm (mostly nothing, so the regeneration and the refill are reached), mixed, and harsh (everything at once).</summary>
    private static Gen<HazardRates[]> RatesSequenceGen { get; } = Gen.Frequency(
        (3, Gen.Frequency((8, Gen.Constant(default(HazardRates))), (1, RatesGen)).ArrayOf(StepsPerSequence)),
        (3, Gen.Frequency((2, Gen.Constant(default(HazardRates))), (2, RatesGen)).ArrayOf(StepsPerSequence)),
        (1, RatesGen.ArrayOf(StepsPerSequence)));

    private static Gen<Case> CaseGen { get; } =
        from start in ResourcesGen(ValueGen)
        from stats in StatsGen
        from regen in Gen.Elements(false, true)
        from rates in RatesSequenceGen
        select new Case(start, stats, regen, rates);

    [Fact]
    public void Advance_AnySequence_WithinZeroAndCapacity()
    {
        Kit.ForAll(CaseGen, Check, SequenceCases);

        // The generator must reach the interesting states: steps that hurt, steps with a resource at its capacity and steps in which a resource regenerates. The thresholds are the first
        // approximation (no implementation to measure yet at A1); A6 measures them and raises them to about a quarter of the measured values, never below what proves the states are reached.
        var coverage = new Coverage();
        var samples = CaseGen.Sample(CoverageCases, 10).ToArray();
        Assert.Equal(CoverageCases, samples.Length);
        foreach (var sample in samples)
        {
            Check(sample, coverage);
        }

        Assert.True(coverage.HurtSteps >= 500, $"{coverage.HurtSteps} steps with damage");
        Assert.True(coverage.AtCapacitySteps >= 500, $"{coverage.AtCapacitySteps} resource-steps at the capacity");
        Assert.True(coverage.RegeneratingSteps >= 500, $"{coverage.RegeneratingSteps} resource-steps that regenerated");
        Assert.True(coverage.EmptySteps >= 500, $"{coverage.EmptySteps} resource-steps at zero");
    }

    [Fact]
    public void Advance_ProtectedWithoutHazard_ReachesCapacity()
    {
        var gen =
            from start in ResourcesGen(ValueGen)
            from stats in StatsGen
            select new Case(start, stats, true, []);

        Kit.ForAll(gen, Refill, RefillCases);
    }

    private static void Check(Case c) => Check(c, null);

    private static void Check(Case c, Coverage? coverage)
    {
        var current = c.Start;
        foreach (var rates in c.Rates)
        {
            var result = SuitStep.Advance(current, rates, c.Stats, c.RegenAllowed, _rules, TickRate);
            var next = result.Next;

            Assert.True(next.IsValid, $"invalid suit {next}");
            Assert.True(float.IsFinite(result.Damage) && result.Damage >= 0f, $"damage {result.Damage}");
            Assert.True(result.ExposedMask < 32, $"mask {result.ExposedMask}");
            var hurt = 0f;
            foreach (var resource in Enum.GetValues<SuitResource>())
            {
                var value = next[resource];
                var capacity = c.Stats.Capacity[resource];
                var rate = rates[(HazardKind)(byte)resource];
                Assert.InRange(value, 0f, capacity);

                // The mask has the bit of a resource exactly when it is at zero while its hazard is active, and the damage is the sum of their rates.
                var exposed = value <= 0f && rate > 0f;
                Assert.Equal(exposed, (result.ExposedMask & (1 << ((byte)resource - 1))) != 0);
                hurt += exposed ? _rules.ZeroDamage((HazardKind)(byte)resource) / TickRate : 0f;

                // A resource whose hazard is absent never goes down.
                if (rate == 0f)
                {
                    Assert.True(value >= MathF.Min(MathF.Min(current[resource], capacity), capacity), $"{resource} fell without a hazard");
                }

                if (coverage is not null)
                {
                    coverage.AtCapacitySteps += value >= capacity ? 1 : 0;
                    coverage.EmptySteps += value <= 0f ? 1 : 0;
                    coverage.RegeneratingSteps += rate == 0f && c.RegenAllowed && value > MathF.Min(current[resource], capacity) ? 1 : 0;
                }
            }

            Assert.Equal(hurt, result.Damage, 4);
            if (coverage is not null)
            {
                coverage.HurtSteps += result.Damage > 0f ? 1 : 0;
            }

            current = next;
        }
    }

    private static void Refill(Case c)
    {
        var current = c.Start;
        for (var i = 0; i < RefillTicks; i++)
        {
            current = SuitStep.Advance(current, default, c.Stats, true, _rules, TickRate).Next;
        }

        Assert.Equal(c.Stats.Capacity, current);
    }

    private sealed record Case(SuitResources Start, SuitStats Stats, bool RegenAllowed, HazardRates[] Rates);

    private sealed class Coverage
    {
        public int HurtSteps { get; set; }

        public int AtCapacitySteps { get; set; }

        public int RegeneratingSteps { get; set; }

        public int EmptySteps { get; set; }
    }
}
