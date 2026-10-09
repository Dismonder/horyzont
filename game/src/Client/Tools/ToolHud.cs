using System.Globalization;
using System.Text;

using Godot;

using Horyzont.Client.Tools;
using Horyzont.Core.Intents;

namespace Horyzont.Game.Client.Tools;

/// <summary>
/// A small label for the active tool until the real HUD (K3-18): the tool, its radius or block, the material, the orientation, the hint
/// of the local preview and the last refusal of the server. Texts are translation keys (<c>locale/tools.csv</c>, <c>TOOL_*</c> and
/// <c>REJECT_*</c>) resolved with <c>tr()</c>.
/// </summary>
public sealed partial class ToolHud : CanvasLayer
{
    private readonly ToolNode _tools;
    private readonly StringBuilder _text = new();
    private Label? _label;
    private Label? _crosshair;

    /// <summary>Creates the HUD.</summary>
    /// <param name="tools">The tools to report.</param>
    public ToolHud(ToolNode tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = tools;
        Name = "ToolHud";
        Layer = 9;
    }

    /// <summary>Gets the text on the label (diagnostics and tests).</summary>
    public string Text => _label?.Text ?? string.Empty;

    /// <summary>Translation key of a refusal: <c>REJECT_</c> and the upper snake case name of the reason.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The key.</returns>
    public static string RejectKey(RejectReason reason)
    {
        var name = reason.ToString();
        var key = new StringBuilder("REJECT_", 7 + name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                key.Append('_');
            }

            key.Append(char.ToUpperInvariant(name[i]));
        }

        return key.ToString();
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _label = new Label { GrowVertical = Control.GrowDirection.Begin };
        _label.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _label.OffsetLeft = 12f;
        _label.OffsetBottom = -12f;
        _crosshair = new Label { Text = "+", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _crosshair.SetAnchorsPreset(Control.LayoutPreset.Center);
        foreach (var label in new[] { _label, _crosshair })
        {
            label.AddThemeColorOverride("font_color", Colors.White);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 4);
            AddChild(label);
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_label is null || _tools.Controller is not { } controller)
        {
            return;
        }

        var settings = controller.Settings;
        var preview = controller.Preview;
        _text.Clear();
        if (settings.Tool == ToolKind.TerrainManipulator)
        {
            _text.Append(Tr("TOOL_TERRAIN")).Append("   ").Append(Tr("TOOL_RADIUS")).Append(' ')
                .Append(settings.Radius.ToString("F2", CultureInfo.InvariantCulture)).Append(" m   ").Append(Tr("TOOL_MATERIAL")).Append(' ')
                .Append(MaterialName(settings.FillMaterial)).Append('\n').Append(Tr("TOOL_HINT_TERRAIN"));
        }
        else
        {
            _text.Append(Tr("TOOL_BUILD")).Append("   ").Append(Tr("TOOL_BLOCK")).Append(' ').Append(BlockName(settings.BlockType)).Append("   ")
                .Append(Tr("TOOL_ORIENTATION")).Append(' ').Append(settings.Orientation).Append('\n').Append(Tr("TOOL_HINT_BUILD"));
        }

        _text.Append("\n1 ").Append(Tr("TOOL_TERRAIN")).Append("   2 ").Append(Tr("TOOL_BUILD"));
        if (preview.Kind != ToolPreviewKind.None && preview.Hint != RejectReason.None)
        {
            _text.Append('\n').Append(Tr(RejectKey(preview.Hint)));
        }

        if (controller.WaitingForServer)
        {
            _text.Append('\n').Append(Tr("TOOL_WAITING"));
        }

        if (_tools.Pending is { LastRejection: not RejectReason.None } pending)
        {
            _text.Append('\n').Append(Tr("TOOL_LAST_REFUSAL")).Append(": ").Append(Tr(RejectKey(pending.LastRejection)));
        }

        _label.Text = _text.ToString();
    }

    private string MaterialName(byte id)
    {
        var entry = _tools.Materials?.Entries.FirstOrDefault(e => e.Id == id);
        return entry is { } e ? e.Key : id.ToString(CultureInfo.InvariantCulture);
    }

    private string BlockName(ushort id)
    {
        var entry = _tools.Blocks?.Entries.FirstOrDefault(e => e.Id == id);
        return entry is { } e ? e.Key : id.ToString(CultureInfo.InvariantCulture);
    }
}
