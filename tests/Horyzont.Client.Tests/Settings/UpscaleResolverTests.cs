using Horyzont.Client.Settings;

namespace Horyzont.Client.Tests.Settings;

public class UpscaleResolverTests
{
    [Theory]
    [InlineData("forward_plus", true, true)]
    [InlineData("mobile", false, false)]
    [InlineData("gl_compatibility", false, false)]
    [InlineData("dummy", false, false)]
    [InlineData("", false, false)]
    [InlineData(null, false, false)]
    public void ForRenderingMethod_OnlyForwardPlusHasFsr(string? method, bool fsr1, bool fsr2)
    {
        var caps = UpscaleCapabilities.ForRenderingMethod(method);

        Assert.Equal(fsr1, caps.Fsr1);
        Assert.Equal(fsr2, caps.Fsr2);
    }

    [Theory]
    [InlineData(UpscaleMode.Off, true, true, UpscaleMode.Off)]
    [InlineData(UpscaleMode.Fsr1, true, true, UpscaleMode.Fsr1)]
    [InlineData(UpscaleMode.Fsr2, true, true, UpscaleMode.Fsr2)]
    [InlineData(UpscaleMode.Fsr2, true, false, UpscaleMode.Fsr1)]
    [InlineData(UpscaleMode.Fsr2, false, false, UpscaleMode.Off)]
    [InlineData(UpscaleMode.Fsr1, false, false, UpscaleMode.Off)]
    [InlineData(UpscaleMode.Fsr1, false, true, UpscaleMode.Off)]
    public void Resolve_FallsBackToWhatIsAvailable(UpscaleMode wanted, bool fsr1, bool fsr2, UpscaleMode expected)
    {
        var resolved = UpscaleResolver.Resolve(new GraphicsSettings { Mode = wanted }, new UpscaleCapabilities(fsr1, fsr2));

        Assert.Equal(expected, resolved.Mode);
    }

    [Fact]
    public void Resolve_KeepsScaleAndSharpness_AndClamps()
    {
        var resolved = UpscaleResolver.Resolve(
            new GraphicsSettings { Mode = UpscaleMode.Fsr1, RenderScale = 0.1f, Sharpness = 9f }, new UpscaleCapabilities(true, true));

        Assert.Equal(0.5f, resolved.RenderScale);
        Assert.Equal(2f, resolved.Sharpness);
    }

    [Fact]
    public void With_OverridesOnlyGivenValues()
    {
        var baseline = new GraphicsSettings { Mode = UpscaleMode.Fsr1, RenderScale = 0.7f, Sharpness = 1f };

        var same = baseline.With(null, null, null);
        var changed = baseline.With(UpscaleMode.Fsr2, 0.6f, null);

        Assert.Equal(baseline, same);
        Assert.Equal(new GraphicsSettings { Mode = UpscaleMode.Fsr2, RenderScale = 0.6f, Sharpness = 1f }, changed);
    }
}
