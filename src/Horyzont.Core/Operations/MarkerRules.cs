using System.Numerics;
using System.Text;

using Horyzont.Core.Frames;
using Horyzont.Core.World.Markers;

namespace Horyzont.Core.Operations;

/// <summary>Constants and validation of marker operations (K5-27 decision 2); part of the operation semantics.</summary>
public static class MarkerRules
{
    /// <summary>Most private markers per player.</summary>
    public const int MaxPrivatePerPlayer = 256;

    /// <summary>Most shared markers.</summary>
    public const int MaxShared = 256;

    /// <summary>Longest name in UTF-8 bytes.</summary>
    public const int MaxNameBytes = 32;

    private static readonly UTF8Encoding _strictUtf8 = new(false, true);

    /// <summary>True for a name of 1..32 UTF-8 bytes without control characters that is not only white space.</summary>
    /// <param name="name">The name.</param>
    /// <returns>Whether the name is valid.</returns>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        int bytes;
        try
        {
            bytes = _strictUtf8.GetByteCount(name);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }

        if (bytes is < 1 or > MaxNameBytes)
        {
            return false;
        }

        var hasVisible = false;
        foreach (var c in name)
        {
            if (char.IsControl(c))
            {
                return false;
            }

            hasVisible |= !char.IsWhiteSpace(c);
        }

        return hasVisible;
    }

    /// <summary>True when the position is finite and within the planet frame limit.</summary>
    /// <param name="position">Position in the planet frame.</param>
    /// <returns>Whether the position is valid.</returns>
    public static bool IsValidPosition(Vector3 position) =>
        float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z)
        && position.Length() <= FrameLimits.PlanetMaxCoordinate;

    /// <summary>True when icon and colour are defined values.</summary>
    /// <param name="icon">Icon.</param>
    /// <param name="color">Colour.</param>
    /// <returns>Whether both are valid.</returns>
    public static bool IsValidStyle(MarkerIcon icon, MarkerColor color) => icon <= MarkerIcon.Poi && color <= MarkerColor.Orange;
}
