using Horyzont.Core.Ids;
using Horyzont.Core.Survival;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-33, D17: the decay of food. The first theory is the acceptance criterion of the spec (docs/spec/03-przetrwanie.md, l. 121).</summary>
public sealed class FoodCurveTests
{
    private static readonly VitalsRules _rules = VitalsRules.Default;

    [Theory]
    [InlineData(0.75, 92.0)]
    [InlineData(0.5, 81.0)]
    [InlineData(0.25, 66.0)]
    [InlineData(0.1, 50.0)]
    public void Strength_SpecTable_WithinHalfPoint(double remaining, double expectedPoints)
    {
        // 100 points of full effect give 92, 81, 66 and 50 points at a quarter, a half, three quarters and nine tenths of the time gone, within 0.5 point.
        var points = 100.0 * FoodCurve.Strength(remaining, _rules);

        Assert.InRange(points, expectedPoints - 0.5, expectedPoints + 0.5);
    }

    [Fact]
    public void Strength_FullAndZero_AreOneAndZero()
    {
        Assert.Equal(1.0, FoodCurve.Strength(1.0, _rules));
        Assert.Equal(0.0, FoodCurve.Strength(0.0, _rules));
    }

    [Fact]
    public void Strength_UsesTheExponentOfTheRules()
    {
        // The exponent is a rule, not a constant of the function: 1 gives a linear curve, 0.3 the curve of the spec.
        var linear = _rules with { FoodCurveExponent = 1.0 };

        Assert.Equal(0.25, FoodCurve.Strength(0.25, linear), 12);
        Assert.Equal(Math.Pow(0.25, 0.3), FoodCurve.Strength(0.25, _rules), 12);
    }

    [Fact]
    public void Strength_Monotonic()
    {
        var previous = -1.0;
        for (var i = 0; i <= 100; i++)
        {
            var strength = FoodCurve.Strength(i / 100.0, _rules);
            Assert.True(strength >= previous, $"strength fell at {i} percent");
            previous = strength;
        }
    }

    [Fact]
    public void Strength_ZeroRemaining_Zero()
    {
        // Not 0^0.3 evaluated by luck: no time left means no effect, even for a tiny remainder just above zero the effect is positive.
        Assert.Equal(0.0, FoodCurve.Strength(0.0, _rules));
        Assert.True(FoodCurve.Strength(1e-9, _rules) > 0.0);
    }

    [Fact]
    public void Remaining_Midway_IsTheFractionOfTheDuration()
    {
        // Eaten at tick 100, lasts 600 ticks: at tick 400 half is left, at tick 100 all of it.
        Assert.Equal(0.5, FoodCurve.Remaining(new Tick(100), new Tick(400), 600), 12);
        Assert.Equal(1.0, FoodCurve.Remaining(new Tick(100), new Tick(100), 600), 12);
        Assert.Equal(1.0 / 600.0, FoodCurve.Remaining(new Tick(100), new Tick(699), 600), 12);
    }

    [Fact]
    public void Remaining_Expired_Zero()
    {
        // The last tick of the food is eaten + duration; from it on nothing is left. The accepted counterpart is the tick before.
        Assert.True(FoodCurve.Remaining(new Tick(100), new Tick(699), 600) > 0.0);

        Assert.Equal(0.0, FoodCurve.Remaining(new Tick(100), new Tick(700), 600));
        Assert.Equal(0.0, FoodCurve.Remaining(new Tick(100), new Tick(701), 600));
        Assert.Equal(0.0, FoodCurve.Remaining(new Tick(100), new Tick(ulong.MaxValue), 600));
    }

    [Fact]
    public void Remaining_BeforeEaten_One()
    {
        // An old snapshot or a clock that is behind the tick of eating: the food counts as fresh, not as more than fresh.
        Assert.Equal(1.0, FoodCurve.Remaining(new Tick(100), new Tick(99), 600));
        Assert.Equal(1.0, FoodCurve.Remaining(new Tick(100), new Tick(0), 600));
    }

    [Fact]
    public void Remaining_NoOverflowNearTheEndOfTime()
    {
        // eaten + duration does not fit in a ulong: 50 of 200 ticks are gone, so three quarters remain, and nothing wraps around.
        var eaten = new Tick(ulong.MaxValue - 100);

        Assert.Equal(0.75, FoodCurve.Remaining(eaten, new Tick(ulong.MaxValue - 50), 200), 12);
        Assert.Equal(1.0, FoodCurve.Remaining(eaten, eaten, 200), 12);
    }
}
