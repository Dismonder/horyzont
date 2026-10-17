using System.Globalization;

using Horyzont.Core.Survival;

namespace Horyzont.Client.Survival;

/// <summary>The numbers of the suit HUD and of the suit screen as text (K6-34), engine-independent so they can be tested; the words around them come from the translations.</summary>
public static class SuitHudText
{
    /// <summary>A resource as the suit screen shows it: the value and the capacity as whole numbers rounded down and the drain multiplier, for example <c>75 / 200  x0.6</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="capacity">The capacity.</param>
    /// <param name="drain">The drain multiplier of the modules (1 without any).</param>
    /// <returns>The text.</returns>
    public static string ResourceLine(float value, float capacity, float drain) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{VitalsHudText.Pair(value, capacity)}  x{(float.IsFinite(drain) ? MathF.Round(drain, 2) : 1f).ToString("0.##", CultureInfo.InvariantCulture)}");

    /// <summary>The time left of a generator as <c>mm:ss</c>, rounded up to whole seconds.</summary>
    /// <param name="ticks">The time in ticks.</param>
    /// <param name="tickRate">Ticks per second.</param>
    /// <returns>The text.</returns>
    public static string GeneratorRemaining(ulong ticks, int tickRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);
        var rate = (ulong)tickRate;
        var seconds = (ticks / rate) + (ticks % rate == 0 ? 0UL : 1UL);
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60:00}:{seconds % 60:00}");
    }

    /// <summary>The rays of the shelter that hit, for example <c>21/26</c>.</summary>
    /// <param name="hits">Rays that hit.</param>
    /// <returns>The text.</returns>
    public static string ShelterHits(int hits) => string.Create(CultureInfo.InvariantCulture, $"{hits}/{ShelterDirections.Count}");

    /// <summary>The first resource of the suit that is at zero while its hazard is active, or none.</summary>
    /// <param name="exposedMask">The exposure mask of message 44.</param>
    /// <returns>The resource, or null.</returns>
    public static SuitResource? FirstExposed(byte exposedMask)
    {
        for (var bit = 0; bit < 5; bit++)
        {
            if ((exposedMask & (1 << bit)) != 0)
            {
                return (SuitResource)(bit + 1);
            }
        }

        return null;
    }
}
