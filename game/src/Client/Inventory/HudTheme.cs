using Godot;

namespace Horyzont.Game.Client.Inventory;

/// <summary>
/// The look of the HUD (K6-29W D17, "styl sci-fi"): dark, semi-transparent panels with a thin steel outline, an amber accent for the
/// selection and a teal accent for tools. Built in code, no binary assets.
/// </summary>
public static class HudTheme
{
    /// <summary>Corner radius of panels and slots, in pixels.</summary>
    public const int CornerRadius = 6;

    /// <summary>Panel background <c>#0B1016</c> at 78 % opacity.</summary>
    public static readonly Color Background = new(0x0B / 255f, 0x10 / 255f, 0x16 / 255f, 0.78f);

    /// <summary>Slot background, a little lighter than the panel.</summary>
    public static readonly Color SlotBackground = new(0x14 / 255f, 0x1D / 255f, 0x26 / 255f, 0.82f);

    /// <summary>Panel outline <c>#2A3A48</c>.</summary>
    public static readonly Color Border = new(0x2A / 255f, 0x3A / 255f, 0x48 / 255f);

    /// <summary>Accent <c>#F2A531</c>: the selected slot, bars and the item name.</summary>
    public static readonly Color Accent = new(0xF2 / 255f, 0xA5 / 255f, 0x31 / 255f);

    /// <summary>Second accent <c>#4FD1C5</c>: tools.</summary>
    public static readonly Color AccentTool = new(0x4F / 255f, 0xD1 / 255f, 0xC5 / 255f);

    /// <summary>Text <c>#E6EDF3</c>.</summary>
    public static readonly Color Text = new(0xE6 / 255f, 0xED / 255f, 0xF3 / 255f);

    /// <summary>Secondary text.</summary>
    public static readonly Color Muted = new(0x8B / 255f, 0x9B / 255f, 0xA8 / 255f);

    /// <summary>Warnings such as "Inventory full".</summary>
    public static readonly Color Danger = new(0xE5 / 255f, 0x53 / 255f, 0x4B / 255f);

    /// <summary>A panel with the outline of the theme.</summary>
    /// <param name="border">Outline colour; <see cref="Border"/> when null.</param>
    /// <param name="borderWidth">Outline width in pixels.</param>
    /// <param name="background">Fill; <see cref="Background"/> when null.</param>
    /// <param name="margin">Inner margin in pixels.</param>
    /// <returns>The style box.</returns>
    public static StyleBoxFlat Panel(Color? border = null, int borderWidth = 1, Color? background = null, float margin = 10f)
    {
        var style = new StyleBoxFlat
        {
            BgColor = background ?? Background,
            BorderColor = border ?? Border,
            ShadowColor = new Color(0f, 0f, 0f, 0.35f),
            ShadowSize = 6,
        };
        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(CornerRadius);
        style.SetContentMarginAll(margin);
        return style;
    }

    /// <summary>Styles a label.</summary>
    /// <param name="label">The label.</param>
    /// <param name="size">Font size in pixels.</param>
    /// <param name="color">Font colour; <see cref="Text"/> when null.</param>
    public static void StyleLabel(Label label, int size, Color? color = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Text);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
        label.AddThemeConstantOverride("outline_size", 3);
    }

    /// <summary>Styles a button.</summary>
    /// <param name="button">The button.</param>
    /// <param name="accent">Outline and text colour.</param>
    public static void StyleButton(Button button, Color accent)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.AddThemeStyleboxOverride("normal", Panel(accent, 1, SlotBackground, 8f));
        button.AddThemeStyleboxOverride("hover", Panel(accent, 2, new Color(0.12f, 0.17f, 0.22f, 0.95f), 8f));
        button.AddThemeStyleboxOverride("pressed", Panel(accent, 2, new Color(0.2f, 0.27f, 0.34f, 0.95f), 8f));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeColorOverride("font_color", Text);
        button.AddThemeColorOverride("font_hover_color", accent);
        button.AddThemeFontSizeOverride("font_size", 14);
    }
}
