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
    public static double Remaining(Tick eaten, Tick now, ulong durationTicks) => throw new NotImplementedException("K6-33 A6");

    /// <summary>The strength of the food bonuses for a remaining fraction.</summary>
    /// <param name="remaining">The remaining fraction in [0, 1].</param>
    /// <param name="rules">The rules with the exponent.</param>
    /// <returns>0 for no time left, else <paramref name="remaining"/> to the power of <see cref="VitalsRules.FoodCurveExponent"/>.</returns>
    public static double Strength(double remaining, VitalsRules rules) => throw new NotImplementedException("K6-33 A6");
}
