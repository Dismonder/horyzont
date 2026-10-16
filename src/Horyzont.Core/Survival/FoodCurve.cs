using Horyzont.Core.Ids;

namespace Horyzont.Core.Survival;

/// <summary>The decay of food over time as a pure function of ticks (K6-33, D17); shared by the server and the client.</summary>
public static class FoodCurve
{
    /// <summary>The fraction of the food time that remains.</summary>
    /// <param name="eaten">The tick the food was eaten at.</param>
    /// <param name="now">The current tick; a tick before <paramref name="eaten"/> gives 1.</param>
    /// <param name="durationTicks">Ticks the food lasts; 0 means nothing is left.</param>
    /// <returns>A value in [0, 1]; 0 for a duration of 0 (no division by zero).</returns>
    public static double Remaining(Tick eaten, Tick now, ulong durationTicks)
    {
        if (durationTicks == 0)
        {
            return 0.0;
        }

        // Subtraction only (never eaten + duration, which can overflow near the end of time).
        if (now.Value <= eaten.Value)
        {
            return 1.0;
        }

        var elapsed = now.Value - eaten.Value;
        return elapsed >= durationTicks ? 0.0 : (durationTicks - elapsed) / (double)durationTicks;
    }

    /// <summary>The strength of the food bonuses for a remaining fraction.</summary>
    /// <param name="remaining">The remaining fraction in [0, 1].</param>
    /// <param name="rules">The rules with the exponent.</param>
    /// <returns>0 for no time left, else <paramref name="remaining"/> to the power of <see cref="VitalsRules.FoodCurveExponent"/>.</returns>
    public static double Strength(double remaining, VitalsRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return remaining <= 0.0 ? 0.0 : Math.Pow(Math.Min(remaining, 1.0), rules.FoodCurveExponent);
    }
}
