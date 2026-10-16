using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Ids;
using Horyzont.Core.Survival;
using Horyzont.Core.Tests.Serialization;
using Horyzont.Core.World;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-33, D16: whatever happens to a player, the vitals stay finite and inside their limits, the exhaustion flag agrees with the stamina, a player
/// who is left alone reaches the maxima, and the generator really reaches the exhaustion and the recovery from it (review A1, P1-2).
/// </summary>
public sealed class VitalsStepProperties
{
    private const int TickRate = 60;
    private const int SequenceCases = 1000;
    private const int RestCases = 100;
    private const int StepsPerSequence = 200;
    private const int CoverageCases = 40;

    /// <summary>The health regeneration is at least 0.5 per second and the largest maximum is 200, so 199 / 0.5 = 398 s = 23880 ticks suffice.</summary>
    private const int RestTicks = 25000;

    private static readonly VitalsRules _rules = VitalsRules.Default;

    private static Gen<FoodBonus> BonusGen { get; } =
        from health in Gen.Choose(0, 100)
        from stamina in Gen.Choose(0, 100)
        from regen in Gen.Choose(0, 8)
        select new FoodBonus(health, stamina, regen / 4f);

    /// <summary>
    /// What happens in one tick. Six in ten ticks are idle, so the regeneration and the recovery from exhaustion are reached; a rare heavy tick (damage
    /// 400, a cost of 40) reaches the floor of the health and the exhaustion at once.
    /// </summary>
    private static Gen<TickEvent> EventGen { get; } = Gen.Frequency(
        (6, Gen.Constant(new TickEvent(false, false, 0f, 0f))),
        (3, from sprinting in Gen.Elements(false, true)
            from jumped in Gen.Elements(false, false, true)
            from extra in Gen.Elements(0f, 0f, 4f, 4f, 12f)
            from damage in Gen.Elements(0f, 0f, 0f, 0.5f, 8f, 60f)
            select new TickEvent(sprinting, jumped, extra, damage)),
        (1, from extra in Gen.Elements(0f, 40f)
            from damage in Gen.Elements(0f, 400f)
            select new TickEvent(false, false, extra, damage)));

    private static Gen<PlayerVitals> StartGen { get; } =
        from health in Gen.Choose(1, 300)
        from stamina in Gen.Choose(0, 300)
        from rested in Gen.Choose(0, 3)
        from exhausted in Gen.Elements(false, true)
        select new PlayerVitals(health, stamina, new Tick((ulong)rested * 400UL), exhausted);

    private static Gen<Case> CaseGen { get; } =
        from start in StartGen
        from bonus in BonusGen
        from events in EventGen.ArrayOf(StepsPerSequence)
        select new Case(start, bonus, events);

    [Fact]
    public void Advance_AnySequence_ValuesFiniteWithinZeroAndMax()
    {
        Kit.ForAll(CaseGen, Check, SequenceCases);

        // The generator must reach the interesting states: an exhausted player, damage that hits the floor, a player at the maxima, and above all the way
        // back from exhaustion (the point of D16). The thresholds are tuned to the generator, never lowered below what proves the states are reached.
        var coverage = new Coverage();
        foreach (var sample in CaseGen.Sample(StepsPerSequence, CoverageCases))
        {
            Check(sample, coverage);
        }

        Assert.True(coverage.ExhaustedSteps >= 200, $"{coverage.ExhaustedSteps} exhausted steps");
        Assert.True(coverage.HealthAtFloor >= 50, $"{coverage.HealthAtFloor} steps at the health floor");
        Assert.True(coverage.StaminaAtMax >= 200, $"{coverage.StaminaAtMax} steps at full stamina");
        Assert.True(coverage.RecoveredFromExhaustion >= 20, $"{coverage.RecoveredFromExhaustion} recoveries from exhaustion");
    }

    [Fact]
    public void Advance_NoUseNoDamage_ReachesMax()
    {
        var restGen =
            from start in StartGen
            from bonus in BonusGen
            select new Case(start, bonus, []);

        Kit.ForAll(restGen, Rest, RestCases);
    }

    private static void Check(Case c) => Check(c, null);

    private static void Check(Case c, Coverage? coverage)
    {
        var vitals = c.Start;
        ulong now = 1000;
        var lastCost = default(Tick);
        foreach (var e in c.Events)
        {
            // The caller (the server) remembers the tick of the last cost; the step decides when the regeneration resumes.
            var input = new VitalsTickInput(e.Sprinting, e.Jumped, e.Extra, e.Damage, lastCost);
            var before = vitals;
            var result = VitalsStep.Advance(vitals, input, c.Bonus, _rules, TickRate, new Tick(now));
            vitals = result.Vitals;
            if (e.Sprinting || e.Jumped || e.Extra > 0f)
            {
                lastCost = new Tick(now);
            }

            now++;

            Assert.True(vitals.IsValid, $"invalid vitals {vitals}");
            Assert.Equal(_rules.BaseHealth + c.Bonus.Health, result.HealthMax);
            Assert.Equal(_rules.BaseStamina + c.Bonus.Stamina, result.StaminaMax);
            Assert.InRange(vitals.Health, _rules.MinHealthWithoutDeath, result.HealthMax);
            Assert.InRange(vitals.Stamina, 0f, result.StaminaMax);

            // Stamina 0 is always exhausted; an exhausted player has less than the recovery line (a small margin for float rounding on the line itself).
            Assert.True(vitals.Stamina > 0f || vitals.Exhausted, "stamina 0 must exhaust");
            Assert.True(!vitals.Exhausted || vitals.Stamina < (_rules.ExhaustedRecoverFraction * result.StaminaMax) + 0.001f, "exhausted above the recovery line");
            if (coverage is not null)
            {
                coverage.ExhaustedSteps += vitals.Exhausted ? 1 : 0;
                coverage.HealthAtFloor += vitals.Health <= _rules.MinHealthWithoutDeath ? 1 : 0;
                coverage.StaminaAtMax += vitals.Stamina >= result.StaminaMax ? 1 : 0;
                coverage.RecoveredFromExhaustion += before.Exhausted && !vitals.Exhausted ? 1 : 0;
            }
        }
    }

    private static void Rest(Case c)
    {
        var vitals = c.Start;
        VitalsStepResult result = default;
        for (var i = 0; i < RestTicks; i++)
        {
            result = VitalsStep.Advance(vitals, default, c.Bonus, _rules, TickRate, new Tick(1000UL + (ulong)i));
            vitals = result.Vitals;
        }

        Assert.Equal(result.HealthMax, vitals.Health);
        Assert.Equal(result.StaminaMax, vitals.Stamina);
        Assert.False(vitals.Exhausted);
    }

    private sealed record TickEvent(bool Sprinting, bool Jumped, float Extra, float Damage);

    private sealed record Case(PlayerVitals Start, FoodBonus Bonus, TickEvent[] Events);

    private sealed class Coverage
    {
        public int ExhaustedSteps { get; set; }

        public int HealthAtFloor { get; set; }

        public int StaminaAtMax { get; set; }

        public int RecoveredFromExhaustion { get; set; }
    }
}
