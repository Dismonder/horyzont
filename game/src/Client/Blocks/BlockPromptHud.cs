using Godot;

using Horyzont.Client.Blocks;
using Horyzont.Game.Client.Tools;

namespace Horyzont.Game.Client.Blocks;

/// <summary>
/// The hint under the crosshair for the block in reach that reacts to the interaction key (K6-30): "[E] Open the door", "Private
/// chest", "Your bed" and so on. The texts are translation keys (<c>locale/tools.csv</c>, <c>PROMPT_*</c>); nothing shows while the
/// inventory screen is open or no block is aimed at. The look is a plain outlined label like the rest of the tool HUD.
/// </summary>
public sealed partial class BlockPromptHud : CanvasLayer
{
    private readonly ToolNode _tools;
    private Label? _label;
    private BlockPrompt? _shown;

    /// <summary>Creates the HUD.</summary>
    /// <param name="tools">The tools whose interaction key is described.</param>
    public BlockPromptHud(ToolNode tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = tools;
        Name = "BlockPromptHud";
        Layer = 9;
    }

    /// <summary>Gets the text on show; empty when there is no hint.</summary>
    public string Text => _label is { Visible: true } label ? label.Text : string.Empty;

    /// <summary>Translation key of the hint of a prompt kind.</summary>
    /// <param name="kind">What the key does.</param>
    /// <returns>The key in <c>tools.csv</c>.</returns>
    public static string KeyOf(BlockPromptKind kind) => kind switch
    {
        BlockPromptKind.OpenChest => "PROMPT_OPEN_CHEST",
        BlockPromptKind.ChestLocked => "PROMPT_CHEST_LOCKED",
        BlockPromptKind.OpenDoor => "PROMPT_OPEN_DOOR",
        BlockPromptKind.CloseDoor => "PROMPT_CLOSE_DOOR",
        BlockPromptKind.SwitchLightOn => "PROMPT_LIGHT_ON",
        BlockPromptKind.SwitchLightOff => "PROMPT_LIGHT_OFF",
        BlockPromptKind.Sleep => "PROMPT_SLEEP",
        BlockPromptKind.OwnBed => "PROMPT_OWN_BED",
        BlockPromptKind.BedOccupied => "PROMPT_BED_OCCUPIED",
        BlockPromptKind.OpenStation => "PROMPT_STATION_OPEN",
        _ => "PROMPT_UNKNOWN",
    };

    /// <summary>Translation key of the hint of a prompt; a station names itself ("[E] Furnace").</summary>
    /// <param name="prompt">The prompt under the crosshair.</param>
    /// <returns>The key in <c>tools.csv</c>.</returns>
    public static string KeyOfPrompt(BlockPrompt prompt) =>
        prompt.Kind == BlockPromptKind.OpenStation ? "PROMPT_STATION_" + prompt.Station.ToString().ToUpperInvariant() : KeyOf(prompt.Kind);

    /// <inheritdoc />
    public override void _Ready()
    {
        _label = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center, GrowHorizontal = Control.GrowDirection.Both };
        _label.SetAnchorsPreset(Control.LayoutPreset.Center);
        _label.OffsetTop = 44f; // under the crosshair of the tool HUD
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
            _shown = null;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_label is null)
        {
            return;
        }

        var prompt = _tools.Inventory?.IsOpen == true ? null : _tools.Interaction?.Prompt;
        if (prompt == _shown && (prompt is null) != _label.Visible)
        {
            return;
        }

        _shown = prompt;
        _label.Visible = prompt is not null;
        if (prompt is { } shown)
        {
            _label.Text = Tr(KeyOfPrompt(shown));
        }
    }
}
