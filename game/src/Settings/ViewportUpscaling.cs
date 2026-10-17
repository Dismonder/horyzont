using Godot;

using Horyzont.Client.Settings;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Settings;

/// <summary>Applies <see cref="GraphicsSettings"/> to a viewport. Godot ignores FSR outside the Forward+ renderer, so the request is first resolved against what the renderer offers.</summary>
public static partial class ViewportUpscaling
{
    /// <summary>Reads what the running renderer offers.</summary>
    /// <returns>Both FSR modes under Forward+, none under Mobile, Compatibility and the headless dummy renderer.</returns>
    public static UpscaleCapabilities CurrentCapabilities() => UpscaleCapabilities.ForRenderingMethod(RenderingServer.GetCurrentRenderingMethod());

    /// <summary>Sets <c>Scaling3DMode</c>, <c>Scaling3DScale</c> and <c>FsrSharpness</c> of the viewport.</summary>
    /// <param name="viewport">Viewport to configure (the main window for the game).</param>
    /// <param name="requested">Wanted settings.</param>
    /// <param name="capabilities">What the renderer offers.</param>
    /// <param name="logger">Receives the applied settings and a warning when the mode fell back.</param>
    /// <returns>The settings that were applied.</returns>
    public static GraphicsSettings Apply(Viewport viewport, GraphicsSettings requested, UpscaleCapabilities capabilities, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(logger);
        var wanted = requested.Clamp();
        var effective = UpscaleResolver.Resolve(wanted, capabilities);
        if (effective.Mode != wanted.Mode)
        {
            logger.LogUpscaleFallback(wanted.Mode, effective.Mode);
        }

        switch (effective.Mode)
        {
            case UpscaleMode.Fsr1:
                viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr;
                break;
            case UpscaleMode.Fsr2:
                viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr2;
                break;
            default:
                // Off: full resolution; a render scale below 1 would otherwise shrink the picture without an upscaler.
                viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
                effective = effective with { RenderScale = 1f };
                break;
        }

        viewport.Scaling3DScale = effective.RenderScale;
        viewport.FsrSharpness = effective.Sharpness;
        logger.LogUpscaleApplied(effective.Mode, effective.RenderScale, effective.Sharpness);
        return effective;
    }

    /// <summary>Loads <c>user://graphics.cfg</c>, lays the command-line overrides over it (not saved) and applies the result to the viewport.</summary>
    /// <param name="viewport">The main window.</param>
    /// <param name="launch">Command line of the process; null for the menu launch, which has no overrides.</param>
    /// <param name="logger">Log of the process.</param>
    /// <returns>The applied settings.</returns>
    public static GraphicsSettings ApplyAtStart(Viewport viewport, Horyzont.Game.Boot.LaunchOptions? launch, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var fromFile = GraphicsSettingsFile.Load(GraphicsSettingsFile.DefaultPath, logger);
        var requested = launch is null ? fromFile : fromFile.With(launch.Upscale, launch.RenderScale, launch.FsrSharpness);
        return Apply(viewport, requested, CurrentCapabilities(), logger);
    }

    [LoggerMessage(EventId = 9611, Level = LogLevel.Warning, Message = "upscaling '{Wanted}' is not available with this renderer; using '{Used}'")]
    private static partial void LogUpscaleFallback(this ILogger logger, UpscaleMode wanted, UpscaleMode used);

    [LoggerMessage(EventId = 9612, Level = LogLevel.Information, Message = "upscaling: mode {Mode}, render scale {Scale}, FSR sharpness {Sharpness}")]
    private static partial void LogUpscaleApplied(this ILogger logger, UpscaleMode mode, float scale, float sharpness);
}
