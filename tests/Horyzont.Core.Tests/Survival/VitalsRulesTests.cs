using Horyzont.Core.Protocol;
using Horyzont.Core.Survival;

namespace Horyzont.Core.Tests.Survival;

/// <summary>The numbers of <see cref="VitalsRules.Default"/> (K6-33 D14 to D18): the base values of the spec and the tuning of the package, pinned once.</summary>
public sealed class VitalsRulesTests
{
    [Fact]
    public void Defaults_OfK6_33()
    {
        var rules = VitalsRules.Default;
        Assert.True(rules.MaxComfort <= ProtocolConstants.MaxRestComfort);

        // Spec 03: 100 health and 100 stamina, the strength r^0.3, repeating the food at 20 percent, the rest 20 s + 8 min + 1 min per comfort,
        // comfort from the blocks within 10 m, the fall from 11 m/s at 8 points per m/s.
        Assert.Equal(100f, rules.BaseHealth);
        Assert.Equal(100f, rules.BaseStamina);
        Assert.Equal(0.3, rules.FoodCurveExponent);
        Assert.Equal(0.2, rules.FoodRepeatFraction);
        Assert.Equal(1200, rules.RestWarmupTicks);
        Assert.Equal(8, rules.RestBaseMinutes);
        Assert.Equal(1, rules.RestMinutesPerComfort);
        Assert.Equal(10f, rules.RestComfortRangeMeters);
        Assert.Equal(11f, rules.FallSafeSpeed);
        Assert.Equal(8f, rules.FallDamagePerMetrePerSecond);

        // The tuning of the package (DO USTALENIA-4): data, changed here together with the plan.
        Assert.Equal(0.5f, rules.BaseHealthRegenPerSecond);
        Assert.Equal(15f, rules.StaminaRegenPerSecond);
        Assert.Equal(60, rules.StaminaRegenDelayTicks);
        Assert.Equal(8f, rules.SprintStaminaPerSecond);
        Assert.Equal(10f, rules.JumpStamina);
        Assert.Equal(4f, rules.ToolUseStamina);
        Assert.Equal(0.2f, rules.ExhaustedRecoverFraction);
        Assert.Equal(1f, rules.MinHealthWithoutDeath);
        Assert.Equal(0.5f, rules.RestStaminaRegenBonus);
        Assert.Equal(10, rules.MaxComfort);
        Assert.Equal(5f, rules.RestHeatRangeMeters);
        Assert.Equal(16f, rules.RestRoofRayMeters);
        Assert.Equal(3f, rules.RestBedRangeMeters);
        Assert.Equal(0.5f, rules.RestStillnessMeters);
        Assert.Equal(30, rules.RestEvaluateEveryTicks);
        Assert.Equal(6, rules.VitalsSendEveryTicks);
    }

    [Fact]
    public void Defaults_FitTheProtocol()
    {
        // The limits of message 44 are the defaults at 60 ticks per second; the server clamps for any other rate (B1 review P2-5).
        var rules = VitalsRules.Default;

        Assert.True(rules.RestWarmupTicks <= ProtocolConstants.MaxRestWarmupTicks);
        Assert.Equal((ulong)ProtocolConstants.MaxRestRemainingTicks, RestRules.DurationTicks(rules.MaxComfort, rules, 60));
    }

    [Fact]
    public void Defaults_AreTheValuesOfANewInstance()
    {
        // The `with` expressions of the other tests rely on the record: a changed copy leaves the shared default alone.
        var changed = VitalsRules.Default with { MaxComfort = 2 };

        Assert.Equal(2, changed.MaxComfort);
        Assert.Equal(10, VitalsRules.Default.MaxComfort);
        Assert.Equal(new VitalsRules(), VitalsRules.Default);
    }
}
