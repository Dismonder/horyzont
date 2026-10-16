using System.Globalization;

using Godot;

using Horyzont.Client.Props;
using Horyzont.Game.Client.Tools;

namespace Horyzont.Game.Client.Props;

/// <summary>
/// The hint under the crosshair for the prop in reach (K6-31 D19): "[LMB] Fell the tree", "[LMB] Break the rock", or "Needs a tool of tier N"
/// when the tool in hand is too weak, followed by the hits landed on this prop ("3/6", counted locally, informational). The texts are
/// translation keys (<c>locale/tools.csv</c>, <c>PROMPT_PROP_*</c>); nothing shows while the inventory screen is open or no prop is aimed at.
/// A plain outlined label like the rest of the tool HUD; how it looks is for the owner to judge.
/// </summary>
public sealed partial class PropPromptHud : CanvasLayer
{
    private readonly ToolNode _tools;
    private Label? _label;
    private PropPrompt? _shownPrompt;
    private PropProgress? _shownProgress;

    /// <summary>Creates the HUD.</summary>
    /// <param name="tools">The tools whose prop interaction is described.</param>
    public PropPromptHud(ToolNode tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = tools;
        Name = "PropPromptHud";
        Layer = 9;
    }

    /// <summary>Gets the text on show; empty when there is no hint.</summary>
    public string Text => _label is { Visible: true } label ? label.Text : string.Empty;

    /// <summary>Translation key of the hint of a prompt kind.</summary>
    /// <param name="kind">What the press does.</param>
    /// <returns>The key in <c>tools.csv</c>.</returns>
    public static string KeyOf(PropPromptKind kind) => kind switch
    {
        PropPromptKind.CutTree => "PROMPT_PROP_CUT_TREE",
        PropPromptKind.CutBush => "PROMPT_PROP_CUT_BUSH",
        PropPromptKind.BreakRock => "PROMPT_PROP_BREAK_ROCK",
        PropPromptKind.MineCrystals => "PROMPT_PROP_MINE_CRYSTALS",
        PropPromptKind.ToolTierTooLow => "PROMPT_PROP_TOOL_TIER",
        _ => "PROMPT_UNKNOWN",
    };

    /// <inheritdoc />
    public override void _Ready()
    {
        _label = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center, GrowHorizontal = Control.GrowDirection.Both };
        _label.SetAnchorsPreset(Control.LayoutPreset.Center);
        _label.OffsetTop = 44f; // under the crosshair of the tool HUD, where the block hint shows too (never both: a prop and a block are not aimed at together)
        _label.AddThemeColorOverride("font_color", Colors.White);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 4);
        AddChild(_label);
    }

    /// <inheritdoc />
    public override void _Notification(int what)
    {
        if (what == NotificationTranslationChanged)
        {
            _shownPrompt = null;
            _shownProgress = null;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_label is null)
        {
            return;
        }

        var props = _tools.Inventory?.IsOpen == true ? null : _tools.Props;
        var prompt = props?.Prompt;
        var progress = prompt is null ? null : props!.AimedProgress;
        if (prompt == _shownPrompt && progress == _shownProgress && (prompt is null) != _label.Visible)
        {
            return;
        }

        _shownPrompt = prompt;
        _shownProgress = progress;
        _label.Visible = prompt is not null;
        if (prompt is { } shown)
        {
            _label.Text = TextOf(shown, progress);
        }
    }

    private string TextOf(PropPrompt prompt, PropProgress? progress)
    {
        var text = Tr(KeyOf(prompt.Kind));
        if (prompt.Kind == PropPromptKind.ToolTierTooLow)
        {
            return string.Format(CultureInfo.InvariantCulture, text, prompt.RequiredTier);
        }

        return progress is { } hits ? string.Create(CultureInfo.InvariantCulture, $"{text} {hits.Hits}/{hits.Total}") : text;
    }
}
