using System.Globalization;

namespace Horyzont.Client.Navigation;

/// <summary>Formats a distance for the HUD without allocating (K5-27 decision 10): "850 m", "12,3 km", "123 km".</summary>
public static class DistanceFormatter
{
    /// <summary>Largest distance shown in metres.</summary>
    public const double MetreLimit = 1000;

    /// <summary>Largest distance shown with one decimal digit of a kilometre.</summary>
    public const double TenthLimit = 100_000;

    /// <summary>Formats <paramref name="meters"/> into <paramref name="destination"/>.</summary>
    /// <param name="meters">Distance, metres; negative or non-finite values fail.</param>
    /// <param name="destination">Buffer; 16 characters always suffice.</param>
    /// <param name="written">Receives the number of characters written.</param>
    /// <param name="decimalSeparator">Decimal separator of the interface language.</param>
    /// <returns>False when the value is invalid or the buffer is too small.</returns>
    public static bool TryFormat(double meters, Span<char> destination, out int written, char decimalSeparator = '.')
    {
        written = 0;
        if (!double.IsFinite(meters) || meters < 0)
        {
            return false;
        }

        var rounded = Math.Round(meters);
        if (rounded < MetreLimit)
        {
            return TryWrite(rounded, "F0", " m", destination, out written, decimalSeparator);
        }

        var km = meters / 1000d;
        return meters < TenthLimit - 50
            ? TryWrite(km, "F1", " km", destination, out written, decimalSeparator)
            : TryWrite(Math.Round(km), "F0", " km", destination, out written, decimalSeparator);
    }

    private static bool TryWrite(double value, string format, string unit, Span<char> destination, out int written, char decimalSeparator)
    {
        written = 0;
        if (!value.TryFormat(destination, out var count, format, CultureInfo.InvariantCulture) || destination.Length - count < unit.Length)
        {
            return false;
        }

        if (decimalSeparator != '.')
        {
            destination[..count].Replace('.', decimalSeparator);
        }

        unit.CopyTo(destination[count..]);
        written = count + unit.Length;
        return true;
    }
}
