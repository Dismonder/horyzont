using Horyzont.Core.Ids;
using Horyzont.Core.Survival;
using Horyzont.Core.World;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-33, D15 and D16: one tick of health and stamina. The order inside a tick is part of the contract (see <see cref="VitalsStep.Advance"/>): costs or
/// regeneration of stamina, regeneration of health, damage, then the clamps and the exhaustion. Defaults: 100 health and stamina, 0.5 health per second,
/// 15 stamina per second after 60 ticks without a cost, 8 per second of sprint, 10 per jump, 60 ticks per second.
/// </summary>
public sealed class VitalsStepTests
{
    private const int TickRate = 60;
    private const double Tolerance = 1e-3;
    private const ulong Start = 1000;

    private static readonly VitalsRules _rules = VitalsRules.Default;
    private static readonly VitalsTickInput _idle = new(false, false, 0f, 0f, false);

    private static VitalsStepResult Step(PlayerVitals vitals, VitalsTickInput input, FoodBonus bonus = default, ulong now = Start) =>
        VitalsStep.Advance(vitals, input, bonus, _rules, TickRate, new Tick(now));

    private static PlayerVitals Run(PlayerVitals vitals, int ticks, VitalsTickInput input, FoodBonus bonus = default)
    {
        for (var i = 0; i < ticks; i++)
        {
            vitals = Step(vitals, input, bonus, Start + (ulong)i).Vitals;
        }

        return vitals;
    }

    [Fact]
    public void Regen_BaseHealthRate()
    {
        // 0.5 per second for one second.
        var after = Run(new PlayerVitals(50f, 100f, default, false), TickRate, _idle);

        Assert.Equal(50.5, after.Health, Tolerance);
        Assert.Equal(100.0, after.Stamina, Tolerance);
        Assert.False(after.Exhausted);
    }

    [Fact]
    public void Regen_FoodAddsRate()
    {
        // 0.5 base plus 1.0 from food is 1.5 per second.
        var after = Run(new PlayerVitals(50f, 100f, default, false), TickRate, _idle, new FoodBonus(0f, 0f, 1.0f));

        Assert.Equal(51.5, after.Health, Tolerance);
    }

    [Fact]
    public void Maxima_AreTheBasePlusTheFoodBonus()
    {
        var plain = Step(new PlayerVitals(50f, 50f, default, false), _idle);
        var fed = Step(new PlayerVitals(50f, 50f, default, false), _idle, new FoodBonus(30f, 12.5f, 0f));

        Assert.Equal(100f, plain.HealthMax);
        Assert.Equal(100f, plain.StaminaMax);
        Assert.Equal(130f, fed.HealthMax);
        Assert.Equal(112.5f, fed.StaminaMax);
    }

    [Fact]
    public void MaxDrops_ValueClamped()
    {
        // The accepted counterpart: with the food the values above the base maximum are legal and stay.
        var fed = Step(new PlayerVitals(110f, 130f, default, false), _idle, new FoodBonus(20f, 40f, 0f));
        Assert.InRange(fed.Vitals.Health, 110f, 120f);
        Assert.InRange(fed.Vitals.Stamina, 130f, 140f);

        // The food has weakened: the maxima fall to the base and the values follow.
        var weak = Step(new PlayerVitals(110f, 130f, default, false), _idle);
        Assert.Equal(100f, weak.HealthMax);
        Assert.Equal(100f, weak.StaminaMax);
        Assert.Equal(100f, weak.Vitals.Health);
        Assert.Equal(100f, weak.Vitals.Stamina);
    }

    [Fact]
    public void Sprint_DrainsPerTick()
    {
        var sprint = new VitalsTickInput(true, false, 0f, 0f, false);

        // 8 per second for one second; no regeneration in a tick that costs, even when the input does not say the stamina was used recently.
        var after = Run(new PlayerVitals(100f, 100f, default, false), TickRate, sprint);

        Assert.Equal(92.0, after.Stamina, Tolerance);
        Assert.False(after.Exhausted);
    }

    [Fact]
    public void Jump_CostsOnce()
    {
        var jump = new VitalsTickInput(false, true, 0f, 0f, false);
        var recent = new VitalsTickInput(false, false, 0f, 0f, true);
        var tick = Step(new PlayerVitals(100f, 100f, default, false), jump).Vitals;

        Assert.Equal(90.0, tick.Stamina, Tolerance);

        // The ticks after the jump cost nothing: while the caller reports the stamina as recently used there is no regeneration, and no second cost.
        Assert.Equal(90.0, Run(tick, 59, recent).Stamina, Tolerance);
    }

    [Fact]
    public void ExtraCost_Applied()
    {
        var tool = new VitalsTickInput(false, false, 4f, 0f, false);

        Assert.Equal(96.0, Step(new PlayerVitals(100f, 100f, default, false), tool).Vitals.Stamina, Tolerance);

        // Sprint, jump and a tool in one tick add up.
        var all = new VitalsTickInput(true, true, 4f, 0f, false);
        Assert.Equal(100.0 - (8.0 / 60.0) - 10.0 - 4.0, Step(new PlayerVitals(100f, 100f, default, false), all).Vitals.Stamina, Tolerance);
    }

    [Fact]
    public void StaminaZero_Exhausted()
    {
        // The accepted counterpart: 3 of 5 leaves 2 and no exhaustion.
        var spared = Step(new PlayerVitals(100f, 5f, default, false), new VitalsTickInput(false, false, 3f, 0f, false)).Vitals;
        Assert.Equal(2.0, spared.Stamina, Tolerance);
        Assert.False(spared.Exhausted);

        var spent = Step(new PlayerVitals(100f, 5f, default, false), new VitalsTickInput(false, false, 10f, 0f, false)).Vitals;

        Assert.Equal(0f, spent.Stamina);
        Assert.True(spent.Exhausted);
    }

    [Fact]
    public void Exhausted_RecoversAt20Percent()
    {
        // 15 per second are 0.25 per tick: 19.75 after 79 ticks (still exhausted), 20.5 after 82 (recovered). Ticks 80 and 81 sit on the 20 percent line
        // and are left to float rounding.
        var vitals = new PlayerVitals(100f, 0f, default, true);
        for (var i = 1; i <= 82; i++)
        {
            vitals = Step(vitals, _idle, default, Start + (ulong)i).Vitals;
            if (i <= 79)
            {
                Assert.True(vitals.Exhausted, $"recovered too early at tick {i}");
            }
        }

        Assert.False(vitals.Exhausted);
        Assert.Equal(20.5, vitals.Stamina, Tolerance);
    }

    [Fact]
    public void Exhausted_StaysWhileStaminaIsSpentAgain()
    {
        // The player cannot sprint when exhausted, but a tool still costs: the cost keeps stamina at zero and the flag set.
        var tool = new VitalsTickInput(false, false, 4f, 0f, true);

        var after = Run(new PlayerVitals(100f, 0f, default, true), 10, tool);

        Assert.Equal(0f, after.Stamina);
        Assert.True(after.Exhausted);
    }

    [Fact]
    public void StaminaRegen_WaitsForDelay()
    {
        var recent = new VitalsTickInput(false, false, 0f, 0f, true);

        // The accepted counterpart: without a recent cost one tick brings 15 / 60 = 0.25.
        Assert.Equal(60.25, Step(new PlayerVitals(100f, 60f, default, false), _idle).Vitals.Stamina, Tolerance);

        Assert.Equal(60.0, Step(new PlayerVitals(100f, 60f, default, false), recent).Vitals.Stamina, Tolerance);
        Assert.Equal(60.0, Run(new PlayerVitals(100f, 60f, default, false), 100, recent).Stamina, Tolerance);
    }

    [Fact]
    public void Rested_StaminaRegen150Percent()
    {
        // Rested while the tick is before RestedUntil: 0.25 x 1.5 = 0.375 per tick; at RestedUntil itself the bonus is over.
        var rested = new PlayerVitals(100f, 10f, new Tick(Start + 1), false);
        var over = new PlayerVitals(100f, 10f, new Tick(Start), false);

        Assert.Equal(10.375, Step(rested, _idle).Vitals.Stamina, Tolerance);
        Assert.Equal(10.25, Step(over, _idle).Vitals.Stamina, Tolerance);
        Assert.Equal(10.25, Step(new PlayerVitals(100f, 10f, default, false), _idle).Vitals.Stamina, Tolerance);

        // The step keeps RestedUntil; ending the bonus is the job of the service, which owns the clock.
        Assert.Equal(new Tick(Start + 1), Step(rested, _idle).Vitals.RestedUntil);
    }

    [Fact]
    public void Damage_NeverBelowOne()
    {
        // The accepted counterpart: 4 damage take health from 10 to about 6 (one tick of regeneration comes first, then the damage).
        var hit = Step(new PlayerVitals(10f, 100f, default, false), new VitalsTickInput(false, false, 0f, 4f, false)).Vitals;
        Assert.InRange(hit.Health, 6f, 6.01f);

        // Until death exists (K6-35) health stops at 1, however large the damage.
        var deep = Step(new PlayerVitals(10f, 100f, default, false), new VitalsTickInput(false, false, 0f, 50f, false)).Vitals;
        Assert.Equal(1f, deep.Health);

        var again = Step(deep, new VitalsTickInput(false, false, 0f, 50f, false)).Vitals;
        Assert.Equal(1f, again.Health);
        Assert.False(again.Exhausted);
    }

    [Fact]
    public void Step_DoesNotTouchWhatItDoesNotOwn()
    {
        var current = new PlayerVitals(50f, 50f, new Tick(5000), true);

        var after = Step(current, _idle).Vitals;

        Assert.Equal(current.RestedUntil, after.RestedUntil);
    }
}
