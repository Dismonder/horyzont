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
}
