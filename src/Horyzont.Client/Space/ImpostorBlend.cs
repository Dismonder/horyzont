namespace Horyzont.Client.Space;

/// <summary>
/// Cross-fade between the impostor of a body and its voxel terrain (K5-22 decision 13, spec 02:133): with the camera altitude
/// <c>h = r − R</c>, the impostor weight is 1 at <c>h ≥ 2 R</c>, 0 at <c>h ≤ R</c> and a smoothstep between; the terrain weight is the
/// complement. Both materials get the same <c>blend</c> uniform and discard complementary pixels of a 4 × 4 Bayer pattern, so each pixel
/// shows exactly one of them. Pure.
/// </summary>
public static class ImpostorBlend
{
    /// <summary>Weight of the impostor for a camera altitude.</summary>
    /// <param name="altitude">Camera altitude over R in metres.</param>
    /// <param name="radius">Surface radius R in metres (positive).</param>
    /// <returns>The weight in [0, 1]; the terrain weight is 1 minus it.</returns>
    public static float ImpostorWeight(double altitude, double radius)
    {
        if (!(radius > 0) || double.IsNaN(altitude))
        {
            return 1f;
        }

        var t = Math.Clamp((altitude - radius) / radius, 0.0, 1.0);
        return (float)(t * t * (3.0 - (2.0 * t)));
    }

    /// <summary>Distinct dither levels of the 4 × 4 Bayer tile (a weight shows as one of <c>Levels + 1</c> patterns).</summary>
    public const int Levels = 16;

    /// <summary>
    /// The weight rounded to one of the <see cref="Levels"/> + 1 patterns the dither can show. The terrain material of a body is swapped
    /// only when this level changes (Voxel Tools copies material parameters into every mesh block), and the impostor uses the same value,
    /// so the two layers stay complementary.
    /// </summary>
    /// <param name="weight">The weight in [0, 1]; NaN counts as 1 (impostor only).</param>
    /// <returns>The quantized weight.</returns>
    public static float Quantize(float weight) =>
        float.IsNaN(weight) ? 1f : MathF.Round(Math.Clamp(weight, 0f, 1f) * Levels) / Levels;

    /// <summary>Hysteresis of <see cref="QuantizeWithHysteresis"/> in levels (each side of the rounding boundary).</summary>
    public const float HysteresisLevels = 0.25f;

    /// <summary>
    /// <see cref="Quantize"/> with a hysteresis of <see cref="HysteresisLevels"/>: the previous level is kept until the weight is more than
    /// half a level plus the hysteresis away from it, so a camera hovering at a rounding boundary does not swap the terrain material every
    /// frame (Opus review of K5-22). Applied once per body and given to both layers, which stay complementary.
    /// </summary>
    /// <param name="weight">The weight in [0, 1]; NaN counts as 1.</param>
    /// <param name="previous">The previous quantized weight; NaN when there is none.</param>
    /// <returns>The quantized weight.</returns>
    public static float QuantizeWithHysteresis(float weight, float previous)
    {
        var quantized = Quantize(weight);
        if (float.IsNaN(previous) || float.IsNaN(weight))
        {
            return quantized;
        }

        var held = Quantize(previous);
        return MathF.Abs((Math.Clamp(weight, 0f, 1f) - held) * Levels) <= 0.5f + HysteresisLevels ? held : quantized;
    }

    /// <summary>Weight of the voxel terrain for a camera altitude (complement of <see cref="ImpostorWeight"/>).</summary>
    /// <param name="altitude">Camera altitude over R in metres.</param>
    /// <param name="radius">Surface radius R in metres.</param>
    /// <returns>The weight in [0, 1].</returns>
    public static float TerrainWeight(double altitude, double radius) => 1f - ImpostorWeight(altitude, radius);

    /// <summary>
    /// Whether the impostor draws a pixel: the threshold of the 4 × 4 Bayer matrix at the pixel is below the impostor weight; the
    /// terrain draws exactly the other pixels (the same formula in both shaders).
    /// </summary>
    /// <param name="x">Pixel column.</param>
    /// <param name="y">Pixel row.</param>
    /// <param name="impostorWeight">The weight from <see cref="ImpostorWeight"/>.</param>
    /// <returns>True when the impostor draws the pixel.</returns>
    public static bool ImpostorDraws(int x, int y, float impostorWeight)
    {
        ReadOnlySpan<byte> bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
        var threshold = (bayer[((y & 3) * 4) + (x & 3)] + 0.5f) / 16f;
        return threshold < impostorWeight;
    }
}
