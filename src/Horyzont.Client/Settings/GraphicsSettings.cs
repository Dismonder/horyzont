namespace Horyzont.Client.Settings;

/// <summary>Graphics settings of the client: upscaling mode, render scale and FSR sharpness. Immutable; <see cref="Clamp"/> brings any value into range.</summary>
public sealed record GraphicsSettings
{
    /// <summary>Lowest render scale.</summary>
    public const float MinRenderScale = 0.5f;

    /// <summary>Highest render scale.</summary>
    public const float MaxRenderScale = 1f;

    /// <summary>Highest FSR sharpness (Godot passes it through unchanged: 0 is the sharpest).</summary>
    public const float MaxSharpness = 2f;

    /// <summary>Gets the default settings: no upscaling, full resolution, sharpness 0.2.</summary>
    public static GraphicsSettings Default { get; } = new();

    /// <summary>Gets the upscaling mode.</summary>
    public UpscaleMode Mode { get; init; } = UpscaleMode.Off;

    /// <summary>Gets the render scale (0.5 to 1.0).</summary>
    public float RenderScale { get; init; } = 1f;

    /// <summary>Gets the FSR sharpness (0 to 2).</summary>
    public float Sharpness { get; init; } = 0.2f;

    /// <summary>Parses a mode name (off, fsr1, fsr2; case-insensitive).</summary>
    /// <param name="text">Name, or null.</param>
    /// <param name="mode">The mode when the name is known.</param>
    /// <returns>True when the name is known.</returns>
    public static bool TryParseMode(string? text, out UpscaleMode mode)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "off":
                mode = UpscaleMode.Off;
                return true;
            case "fsr1":
                mode = UpscaleMode.Fsr1;
                return true;
            case "fsr2":
                mode = UpscaleMode.Fsr2;
                return true;
            default:
                mode = UpscaleMode.Off;
                return false;
        }
    }

    /// <summary>Gets the canonical lower-case name of a mode.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>off, fsr1 or fsr2.</returns>
    public static string ModeName(UpscaleMode mode) => mode switch
    {
        UpscaleMode.Fsr1 => "fsr1",
        UpscaleMode.Fsr2 => "fsr2",
        _ => "off",
    };

    /// <summary>Tells whether a number is a valid render scale.</summary>
    /// <param name="value">Candidate.</param>
    /// <returns>True when finite and within range.</returns>
    public static bool IsValidRenderScale(float value) => float.IsFinite(value) && value is >= MinRenderScale and <= MaxRenderScale;

    /// <summary>Tells whether a number is a valid sharpness.</summary>
    /// <param name="value">Candidate.</param>
    /// <returns>True when finite and within range.</returns>
    public static bool IsValidSharpness(float value) => float.IsFinite(value) && value is >= 0f and <= MaxSharpness;

    /// <summary>Returns a copy in which every given value replaces the current one (command-line overrides); null keeps it.</summary>
    /// <param name="mode">Mode override.</param>
    /// <param name="renderScale">Render scale override.</param>
    /// <param name="sharpness">Sharpness override.</param>
    /// <returns>The changed copy.</returns>
    public GraphicsSettings With(UpscaleMode? mode, float? renderScale, float? sharpness) => this with
    {
        Mode = mode ?? Mode,
        RenderScale = renderScale ?? RenderScale,
        Sharpness = sharpness ?? Sharpness,
    };

    /// <summary>Returns the settings with every value brought into range: unknown modes and non-finite numbers fall back to the default, other numbers are clamped.</summary>
    /// <returns>Valid settings.</returns>
    public GraphicsSettings Clamp() => new()
    {
        Mode = Enum.IsDefined(Mode) ? Mode : Default.Mode,
        RenderScale = float.IsFinite(RenderScale) ? Math.Clamp(RenderScale, MinRenderScale, MaxRenderScale) : Default.RenderScale,
        Sharpness = float.IsFinite(Sharpness) ? Math.Clamp(Sharpness, 0f, MaxSharpness) : Default.Sharpness,
    };
}
