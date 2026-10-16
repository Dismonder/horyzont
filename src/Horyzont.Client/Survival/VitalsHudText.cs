using System.Globalization;

using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Progress;
using Horyzont.Core.Survival;

namespace Horyzont.Client.Survival;

/// <summary>The numbers of the vitals HUD as text (K6-33), engine-independent so they can be tested; the words around them come from the translations.</summary>
public static class VitalsHudText
{
    /// <summary>A value and its maximum as whole numbers, rounded down: <c>85 / 110</c>.</summary>
    /// <param name="value">The current value.</param>
    /// <param name="max">The maximum.</param>
    /// <returns>The text.</returns>
    public static string Pair(float value, float max) =>
        string.Create(CultureInfo.InvariantCulture, $"{Whole(value)} / {Whole(max)}");

    /// <summary>A number rounded down to a whole number, 0 for NaN, infinity and negative values.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The whole number.</returns>
    public static int Whole(float value) => float.IsFinite(value) && value > 0f ? (int)MathF.Floor(Math.Min(value, int.MaxValue)) : 0;

    /// <summary>A time as whole minutes (rounded down) from one minute, as whole seconds below it.</summary>
    /// <param name="ticks">The time in ticks.</param>
    /// <param name="tickRate">Ticks per second.</param>
    /// <returns>For example <c>11 min</c> or <c>40 s</c>.</returns>
    public static string Duration(ulong ticks, int tickRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);
        var seconds = ticks / (ulong)tickRate;
        return seconds >= 60
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 60} min")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds} s");
    }

    /// <summary>The ticks left of the food in a slot.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="food">The statistics of the food, null when the type is no food.</param>
    /// <param name="now">The current tick of the server.</param>
    /// <param name="tickRate">Ticks per second.</param>
    /// <returns>0 for an empty slot, an unknown food and a finished one.</returns>
    public static ulong FoodRemainingTicks(FoodSlot slot, FoodDef? food, Tick now, int tickRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);
        if (slot.IsEmpty || food is null)
        {
            return 0;
        }

        var duration = food.DurationSeconds * (ulong)tickRate;
        var passed = now.Value > slot.Eaten.Value ? now.Value - slot.Eaten.Value : 0UL;
        return passed >= duration ? 0UL : duration - passed;
    }

    /// <summary>The fraction of the food time that remains, for the bar of a slot.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="food">The statistics of the food.</param>
    /// <param name="now">The current tick of the server.</param>
    /// <param name="tickRate">Ticks per second.</param>
    /// <returns>0 to 1; the same curve the server uses.</returns>
    public static double FoodFraction(FoodSlot slot, FoodDef? food, Tick now, int tickRate) =>
        slot.IsEmpty || food is null ? 0.0 : FoodCurve.Remaining(slot.Eaten, now, food.DurationSeconds * (ulong)tickRate);
}
