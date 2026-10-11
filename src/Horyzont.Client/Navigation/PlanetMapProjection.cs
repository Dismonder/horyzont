using System.Numerics;

namespace Horyzont.Client.Navigation;

/// <summary>
/// Equirectangular projection of the planet map (K5-27 decision 12): u runs over the longitude from west to east, v over the latitude
/// from the north pole (0) to the south pole (1). The planet frame has +Y towards the north pole and longitude 0 along +X.
/// </summary>
public static class PlanetMapProjection
{
    /// <summary>Columns of the height grid.</summary>
    public const int Width = 128;

    /// <summary>Rows of the height grid.</summary>
    public const int Height = 64;

    /// <summary>Unit direction of a map point.</summary>
    /// <param name="u">Horizontal position, 0..1.</param>
    /// <param name="v">Vertical position, 0..1.</param>
    /// <returns>The direction in the planet frame.</returns>
    public static Vector3 Direction(float u, float v)
    {
        var longitude = (u * MathF.Tau) - MathF.PI;
        var latitude = (MathF.PI * 0.5f) - (v * MathF.PI);
        var cosLatitude = MathF.Cos(latitude);
        return new Vector3(cosLatitude * MathF.Cos(longitude), MathF.Sin(latitude), cosLatitude * MathF.Sin(longitude));
    }

    /// <summary>Map point of a position in the planet frame.</summary>
    /// <param name="position">Position, not at the centre.</param>
    /// <param name="u">Receives the horizontal position.</param>
    /// <param name="v">Receives the vertical position.</param>
    /// <returns>False for a position at the centre or a non-finite one.</returns>
    public static bool TryFromPosition(Vector3 position, out float u, out float v)
    {
        u = 0;
        v = 0;
        var length = position.Length();
        if (!float.IsFinite(length) || length < 1e-3f)
        {
            return false;
        }

        var longitude = MathF.Atan2(position.Z, position.X);
        var latitude = MathF.Asin(Math.Clamp(position.Y / length, -1f, 1f));
        u = (longitude + MathF.PI) / MathF.Tau;
        v = ((MathF.PI * 0.5f) - latitude) / MathF.PI;
        return true;
    }
}
