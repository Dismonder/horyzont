using Horyzont.Client.Space;

namespace Horyzont.Client.Tests.Space;

public sealed class ImpostorBlendTests
{
    [Fact]
    public void ImpostorBlend_Boundaries_MonotonicAndComplementary()
    {
        const double r = 3000;
        Assert.Equal(1f, ImpostorBlend.ImpostorWeight(2 * r, r));
        Assert.Equal(1f, ImpostorBlend.ImpostorWeight(5 * r, r));
        Assert.Equal(0f, ImpostorBlend.ImpostorWeight(r, r));
        Assert.Equal(0f, ImpostorBlend.ImpostorWeight(10, r));
        var previous = -1f;
        for (var h = 0.0; h <= 3 * r; h += r / 64)
        {
            var impostor = ImpostorBlend.ImpostorWeight(h, r);
            Assert.True(impostor >= previous);
            Assert.Equal(1f, impostor + ImpostorBlend.TerrainWeight(h, r), 5);
            previous = impostor;
        }

        // Dithering: over a 4 x 4 tile the impostor draws round(16 w) pixels and the terrain the others, never both.
        foreach (var w in new[] { 0f, 0.25f, 0.5f, 0.8f, 1f })
        {
            var drawn = 0;
            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    drawn += ImpostorBlend.ImpostorDraws(x, y, w) ? 1 : 0;
                }
            }

            Assert.Equal((int)Math.Round(16 * w), drawn);
        }
    }

    [Fact]
    public void ImpostorBlend_Quantize_SameDitherAsContinuous_17Levels()
    {
        var levels = new HashSet<float>();
        for (var i = 0; i <= 1000; i++)
        {
            var weight = i / 1000f;
            var quantized = ImpostorBlend.Quantize(weight);
            levels.Add(quantized);
            Assert.InRange(MathF.Abs(quantized - weight), 0f, (0.5f / 16f) + 1e-6f);

            // The impostor draws exactly 16 · q of the 16 pixels of the Bayer tile (the terrain the others).
            var drawn = 0;
            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    drawn += ImpostorBlend.ImpostorDraws(x, y, quantized) ? 1 : 0;
                }
            }

            Assert.Equal((int)MathF.Round(quantized * 16f), drawn);
        }

        Assert.Equal(ImpostorBlend.Levels + 1, levels.Count);
        Assert.Equal(0f, ImpostorBlend.Quantize(0f));
        Assert.Equal(1f, ImpostorBlend.Quantize(1f));
        Assert.Equal(1f, ImpostorBlend.Quantize(float.NaN));
    }

    [Fact]
    public void ImpostorBlend_QuantizeWithHysteresis_HoldsTheLevelWithinAQuarterLevel()
    {
        const float step = 1f / ImpostorBlend.Levels;

        // From level 8, the plain rounding boundary is 8.5; the level changes only past 8.75 (or below 7.25).
        Assert.Equal(8 * step, ImpostorBlend.QuantizeWithHysteresis(8.6f * step, 8 * step));
        Assert.Equal(8 * step, ImpostorBlend.QuantizeWithHysteresis(8.74f * step, 8 * step));
        Assert.Equal(9 * step, ImpostorBlend.QuantizeWithHysteresis(8.76f * step, 8 * step));
        Assert.Equal(8 * step, ImpostorBlend.QuantizeWithHysteresis(7.3f * step, 8 * step));
        Assert.Equal(7 * step, ImpostorBlend.QuantizeWithHysteresis(7.2f * step, 8 * step));

        // A camera jittering around a boundary never flips the level back and forth.
        var level = ImpostorBlend.Quantize(8.4f * step);
        var changes = 0;
        for (var i = 0; i < 100; i++)
        {
            var next = ImpostorBlend.QuantizeWithHysteresis((i % 2 == 0 ? 8.4f : 8.6f) * step, level);
            changes += next == level ? 0 : 1;
            level = next;
        }

        Assert.Equal(0, changes);

        // Jumps and the ends are exact; NaN previous means no history.
        Assert.Equal(1f, ImpostorBlend.QuantizeWithHysteresis(1f, 0f));
        Assert.Equal(0f, ImpostorBlend.QuantizeWithHysteresis(0f, 1f));
        Assert.Equal(ImpostorBlend.Quantize(0.53f), ImpostorBlend.QuantizeWithHysteresis(0.53f, float.NaN));
        Assert.Equal(1f, ImpostorBlend.QuantizeWithHysteresis(float.NaN, 0f));
    }
}
