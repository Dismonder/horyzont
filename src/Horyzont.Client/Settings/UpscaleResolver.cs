namespace Horyzont.Client.Settings;

/// <summary>Which upscalers the running renderer offers (Godot honours FSR only in the Forward+ renderer).</summary>
/// <param name="Fsr1">Whether FSR 1.0 works.</param>
/// <param name="Fsr2">Whether FSR 2.2 works.</param>
public readonly record struct UpscaleCapabilities(bool Fsr1, bool Fsr2)
{
    /// <summary>Gets the capabilities of a renderer that offers no FSR (Mobile, Compatibility, headless).</summary>
    public static UpscaleCapabilities None { get; } = new(false, false);

    /// <summary>Derives the capabilities from the rendering method name.</summary>
    /// <param name="renderingMethod">Result of <c>RenderingServer.GetCurrentRenderingMethod()</c>: forward_plus, mobile, gl_compatibility or dummy.</param>
    /// <returns>Both FSR modes for forward_plus, none otherwise.</returns>
    public static UpscaleCapabilities ForRenderingMethod(string? renderingMethod) =>
        renderingMethod == "forward_plus" ? new UpscaleCapabilities(true, true) : None;
}

/// <summary>Brings graphics settings in line with what the renderer can do.</summary>
public static class UpscaleResolver
{
    /// <summary>Clamps the settings and replaces an unavailable mode by the best available one: FSR2 falls back to FSR1, FSR1 to Off.</summary>
    /// <param name="settings">Requested settings.</param>
    /// <param name="capabilities">What the renderer offers.</param>
    /// <returns>Settings that are valid and usable.</returns>
    public static GraphicsSettings Resolve(GraphicsSettings settings, UpscaleCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var clamped = settings.Clamp();
        var mode = clamped.Mode;
        if (mode == UpscaleMode.Fsr2 && !capabilities.Fsr2)
        {
            mode = UpscaleMode.Fsr1;
        }

        if (mode == UpscaleMode.Fsr1 && !capabilities.Fsr1)
        {
            mode = UpscaleMode.Off;
        }

        return clamped with { Mode = mode };
    }
}
