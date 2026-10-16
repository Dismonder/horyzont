namespace Horyzont.Core.Survival;

/// <summary>How long the rested bonus lasts (K6-33, D14).</summary>
public static class RestRules
{
    /// <summary>The duration of the rested bonus.</summary>
    /// <param name="comfort">The comfort level; above <see cref="VitalsRules.MaxComfort"/> it counts as the maximum.</param>
    /// <param name="rules">The rules.</param>
    /// <param name="tickRate">Ticks per second.</param>
    /// <returns><c>(RestBaseMinutes + comfort * RestMinutesPerComfort) * 60 * tickRate</c> ticks.</returns>
    public static ulong DurationTicks(int comfort, VitalsRules rules, int tickRate) => throw new NotImplementedException("K6-33 A6");
}
