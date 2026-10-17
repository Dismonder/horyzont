using Horyzont.Client.Settings;

namespace Horyzont.Client.Tests.Settings;

public class GraphicsSettingsTests
{
    [Fact]
    public void Default_IsOffFullScaleSharpnessPointTwo()
    {
        var d = GraphicsSettings.Default;

        Assert.Equal(UpscaleMode.Off, d.Mode);
        Assert.Equal(1f, d.RenderScale);
        Assert.Equal(0.2f, d.Sharpness);
    }

    [Theory]
    [InlineData(0.1f, 0.5f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0.77f, 0.77f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 1f)]
    [InlineData(float.NaN, 1f)]
    [InlineData(float.PositiveInfinity, 1f)]
    public void Clamp_RenderScale(float input, float expected)
    {
        Assert.Equal(expected, new GraphicsSettings { RenderScale = input }.Clamp().RenderScale);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(1.5f, 1.5f)]
    [InlineData(3f, 2f)]
    [InlineData(float.NaN, 0.2f)]
    [InlineData(float.NegativeInfinity, 0.2f)]
    public void Clamp_Sharpness(float input, float expected)
    {
        Assert.Equal(expected, new GraphicsSettings { Sharpness = input }.Clamp().Sharpness);
    }

    [Fact]
    public void Clamp_UnknownMode_FallsBackToOff()
    {
        Assert.Equal(UpscaleMode.Off, new GraphicsSettings { Mode = (UpscaleMode)42 }.Clamp().Mode);
        Assert.Equal(UpscaleMode.Fsr2, new GraphicsSettings { Mode = UpscaleMode.Fsr2 }.Clamp().Mode);
    }

    [Theory]
    [InlineData("off", UpscaleMode.Off)]
    [InlineData("FSR1", UpscaleMode.Fsr1)]
    [InlineData(" fsr2 ", UpscaleMode.Fsr2)]
    public void TryParseMode_KnownNames(string text, UpscaleMode expected)
    {
        Assert.True(GraphicsSettings.TryParseMode(text, out var mode));
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fsr3")]
    [InlineData("1")]
    public void TryParseMode_UnknownNames_False(string? text)
    {
        Assert.False(GraphicsSettings.TryParseMode(text, out _));
    }

    [Theory]
    [InlineData(UpscaleMode.Off)]
    [InlineData(UpscaleMode.Fsr1)]
    [InlineData(UpscaleMode.Fsr2)]
    public void ModeName_RoundTrips(UpscaleMode mode)
    {
        Assert.True(GraphicsSettings.TryParseMode(GraphicsSettings.ModeName(mode), out var parsed));
        Assert.Equal(mode, parsed);
    }

    [Fact]
    public void Validators_AcceptOnlyFiniteInRange()
    {
        Assert.True(GraphicsSettings.IsValidRenderScale(0.5f));
        Assert.False(GraphicsSettings.IsValidRenderScale(0.49f));
        Assert.False(GraphicsSettings.IsValidRenderScale(float.NaN));
        Assert.True(GraphicsSettings.IsValidSharpness(2f));
        Assert.False(GraphicsSettings.IsValidSharpness(2.01f));
    }
}
