using GdUnit4;

using Godot;

using Horyzont.Client.Settings;
using Horyzont.Game.Settings;

using Microsoft.Extensions.Logging.Abstractions;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Settings;

[TestSuite]
public class ViewportUpscalingTests
{
    private static readonly UpscaleCapabilities _all = new(true, true);

    [TestCase]
    [RequireGodotRuntime]
    public void Apply_Fsr2_SetsViewportProperties()
    {
        var viewport = new SubViewport();
        try
        {
            var effective = ViewportUpscaling.Apply(viewport, new GraphicsSettings { Mode = UpscaleMode.Fsr2, RenderScale = 0.67f, Sharpness = 0.8f }, _all, NullLogger.Instance);

            AssertThat(effective.Mode).IsEqual(UpscaleMode.Fsr2);
            AssertThat(viewport.Scaling3DMode).IsEqual(Viewport.Scaling3DModeEnum.Fsr2);
            AssertThat(viewport.Scaling3DScale).IsEqual(0.67f);
            AssertThat(viewport.FsrSharpness).IsEqual(0.8f);
        }
        finally
        {
            viewport.Free();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Apply_Fsr1_SetsViewportProperties()
    {
        var viewport = new SubViewport();
        try
        {
            ViewportUpscaling.Apply(viewport, new GraphicsSettings { Mode = UpscaleMode.Fsr1, RenderScale = 0.5f, Sharpness = 0f }, _all, NullLogger.Instance);

            AssertThat(viewport.Scaling3DMode).IsEqual(Viewport.Scaling3DModeEnum.Fsr);
            AssertThat(viewport.Scaling3DScale).IsEqual(0.5f);
            AssertThat(viewport.FsrSharpness).IsEqual(0f);
        }
        finally
        {
            viewport.Free();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Apply_Off_RestoresFullResolutionBilinear()
    {
        var viewport = new SubViewport { Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr2, Scaling3DScale = 0.5f };
        try
        {
            ViewportUpscaling.Apply(viewport, new GraphicsSettings { Mode = UpscaleMode.Off, RenderScale = 0.6f }, _all, NullLogger.Instance);

            AssertThat(viewport.Scaling3DMode).IsEqual(Viewport.Scaling3DModeEnum.Bilinear);
            AssertThat(viewport.Scaling3DScale).IsEqual(1f);
        }
        finally
        {
            viewport.Free();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Apply_NoFsrRenderer_FallsBackToOff()
    {
        var viewport = new SubViewport();
        try
        {
            var effective = ViewportUpscaling.Apply(viewport, new GraphicsSettings { Mode = UpscaleMode.Fsr2, RenderScale = 0.6f }, UpscaleCapabilities.None, NullLogger.Instance);

            AssertThat(effective.Mode).IsEqual(UpscaleMode.Off);
            AssertThat(viewport.Scaling3DMode).IsEqual(Viewport.Scaling3DModeEnum.Bilinear);
            AssertThat(viewport.Scaling3DScale).IsEqual(1f);
        }
        finally
        {
            viewport.Free();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Apply_Fsr2Unavailable_FallsBackToFsr1()
    {
        var viewport = new SubViewport();
        try
        {
            var effective = ViewportUpscaling.Apply(viewport, new GraphicsSettings { Mode = UpscaleMode.Fsr2, RenderScale = 0.7f }, new UpscaleCapabilities(true, false), NullLogger.Instance);

            AssertThat(effective.Mode).IsEqual(UpscaleMode.Fsr1);
            AssertThat(viewport.Scaling3DMode).IsEqual(Viewport.Scaling3DModeEnum.Fsr);
        }
        finally
        {
            viewport.Free();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void CurrentCapabilities_MatchesTheRenderingMethod()
    {
        // Headless runs the dummy rendering method, so nothing is offered there; a real Forward+ window is not covered here.
        AssertThat(ViewportUpscaling.CurrentCapabilities()).IsEqual(UpscaleCapabilities.ForRenderingMethod(RenderingServer.GetCurrentRenderingMethod()));
    }
}
