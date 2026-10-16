using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Ids;
using Horyzont.Core.Survival;
using Horyzont.Core.Tests.Serialization;
using Horyzont.Core.World;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-33, D16: whatever happens to a player, the vitals stay finite and inside their limits, the exhaustion flag agrees with the stamina, and a player
/// who is left alone reaches the maxima.
/// </summary>
public sealed class VitalsStepProperties
{
    private const int TickRate = 60;
    private const int SequenceCases = 1000;
    private const int RestCases = 100;
    private const int StepsPerSequence = 200;

    /// <summary>The health regeneration is at least 0.5 per second and the largest maximum is 200, so 199 / 0.5 = 398 s = 23880 ticks suffice.</summary>
    private const int RestTicks = 25000;

    private static readonly VitalsRules _rules = VitalsRules.Default;

    private static Gen<FoodBonus> BonusGen { get; } =
        from health in Gen.Choose(0, 100)
        from stamina in Gen.Choose(0, 100)
        from regen in Gen.Choose(0, 8)
        select new FoodBonus(health, stamina, regen / 4f);

    private static Gen<VitalsTickInput> InputGen { get; } =
        from sprinting in Gen.Elements(false, true)
        from jumped in Gen.Elements(false, true)
        from extra in Gen.Elements(0f, 0f, 4f, 4f, 12f, 40f)
        from damage in Gen.Elements(0f, 0f, 0f, 0.5f, 8f, 60f, 400f)
        from recent in Gen.Elements(false, true)
        select new VitalsTickInput(sprinting, jumped, extra, damage, recent);

    private static Gen<PlayerVitals> StartGen { get; } =
        from health in Gen.Choose(1, 300)
        from stamina in Gen.Choose(0, 300)
        from rested in Gen.Choose(0, 3)
        from exhausted in Gen.Elements(false, true)
        select new PlayerVitals(health, stamina, new Tick((ulong)rested * 400UL), exhausted);

    private static Gen<Case> CaseGen { get; } =
        from start in StartGen
        from bonus in BonusGen
        from inputs in InputGen.ArrayOf(StepsPerSequence)
        select new Case(start, bonus, inputs);

    [Fact]
    public void Advance_AnySequence_ValuesFiniteWithinZeroAndMax()
    {
        Kit.ForAll(CaseGen, Check, SequenceCases);

        // The generator must reach the interesting states: an exhausted player, damage that hits the floor, a player at the maxima.
        var coverage = new Coverage();
        foreach (var sample in CaseGen.Sample(200, 10))
        {
            Check(sample, coverage);
        }

        Assert.True(coverage.ExhaustedSteps >= 200, $"{coverage.ExhaustedSteps} exhausted steps");
        Assert.True(coverage.HealthAtFloor >= 50, $"{coverage.HealthAtFloor} steps at the health floor");
        Assert.True(coverage.StaminaAtMax >= 200, $"{coverage.StaminaAtMax} steps at full stamina");
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
        foreach (var input in c.Inputs)
        {
            var result = VitalsStep.Advance(vitals, input, c.Bonus, _rules, TickRate, new Tick(now++));
            vitals = result.Vitals;

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

    private sealed record Case(PlayerVitals Start, FoodBonus Bonus, VitalsTickInput[] Inputs);

    private sealed class Coverage
    {
        public int ExhaustedSteps { get; set; }

        public int HealthAtFloor { get; set; }

        public int StaminaAtMax { get; set; }
    }
}
