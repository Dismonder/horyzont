using System.Globalization;
using System.Text;

using Godot;

using Horyzont.Client.Ships;
using Horyzont.Client.Tools;
using Horyzont.Core.Intents;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Game.Client.Tools;

/// <summary>
/// A small label for the active tool until the real HUD (K3-18): the tool, its radius or block, the material, the orientation, the hint
/// of the local preview and the last refusal of the server. Texts are translation keys (<c>locale/tools.csv</c>, <c>TOOL_*</c> and
/// <c>REJECT_*</c>) resolved with <c>tr()</c>.
/// </summary>
public sealed partial class ToolHud : CanvasLayer
{
    private const int ToggleMessageTicks = 180;

    private readonly ToolNode _tools;
    private readonly StringBuilder _text = new();
    private readonly Dictionary<string, string> _translations = [];
    private readonly string?[] _rejectTexts = new string?[256];
    private Shown _shown;
    private bool _hasShown;
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
        _label.OffsetBottom = -96f; // above the hotbar
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
    public override void _Notification(int what)
    {
        if (what == NotificationTranslationChanged)
        {
            // New locale: the cached translations and the text built from them are stale.
            _translations.Clear();
            Array.Clear(_rejectTexts);
            _hasShown = false;
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
        var hint = preview.Kind != ToolPreviewKind.None ? preview.Hint : RejectReason.None;
        var waiting = controller.WaitingForServer;
        var refusal = _tools.Pending?.LastRejection ?? RejectReason.None;
        var ship = _tools.Ship?.Aimed;
        var toggle = _tools.Ticks - _tools.LastShipToggleTick < ToggleMessageTicks ? _tools.LastShipToggle : null;
        var stockCount = -1;
        var stockName = string.Empty;
        if (settings.Tool == ToolKind.TerrainManipulator && _tools.Inventory is { } inventory
            && inventory.TryGetFillStock(MaterialName(settings.FillMaterial), out var name, out var count))
        {
            stockCount = count;
            stockName = name;
        }

        var shown = new Shown(
            settings.Tool, settings.Radius, settings.FillMaterial, settings.BlockType, settings.Orientation, hint, waiting, refusal, ship, toggle,
            stockCount, stockName);
        if (_hasShown && shown.Equals(_shown))
        {
            return;
        }

        _shown = shown;
        _hasShown = true;
        _text.Clear();
        if (settings.Tool == ToolKind.None)
        {
            _text.Append(T("TOOL_NONE"));
        }
        else if (settings.Tool == ToolKind.TerrainManipulator)
        {
            _text.Append(T("TOOL_TERRAIN")).Append("   ").Append(T("TOOL_RADIUS")).Append(' ')
                .Append(settings.Radius.ToString("F2", CultureInfo.InvariantCulture)).Append(" m   ").Append(T("TOOL_MATERIAL")).Append(' ')
                .Append(MaterialName(settings.FillMaterial)).Append('\n').Append(T("TOOL_HINT_TERRAIN"));
            if (stockCount >= 0)
            {
                _text.Append('\n').Append(T("TOOL_STOCK")).Append(": ").Append(stockName).Append(' ').Append(stockCount.ToString(CultureInfo.InvariantCulture));
            }
        }
        else
        {
            _text.Append(T("TOOL_BUILD")).Append("   ").Append(T("TOOL_BLOCK")).Append(' ').Append(BlockName(settings.BlockType)).Append("   ")
                .Append(T("TOOL_ORIENTATION")).Append(' ').Append(settings.Orientation).Append('\n').Append(T("TOOL_HINT_BUILD"));
        }

        _text.Append('\n').Append(T("INV_HOTBAR_HINT"));
        if (hint != RejectReason.None)
        {
            _text.Append('\n').Append(RejectText(hint));
        }

        if (waiting)
        {
            _text.Append('\n').Append(T("TOOL_WAITING"));
        }

        if (ship is not null)
        {
            AppendShip(ship);
        }

        if (toggle is ShipModeToggleResult.NotACockpit or ShipModeToggleResult.NoConstruct)
        {
            _text.Append('\n').Append(T("SHIP_NOT_COCKPIT"));
        }

        if (refusal != RejectReason.None)
        {
            _text.Append('\n').Append(T("TOOL_LAST_REFUSAL")).Append(": ").Append(RejectText(refusal));
        }

        var text = _text.ToString();
        if (!string.Equals(_label.Text, text, StringComparison.Ordinal))
        {
            _label.Text = text;
        }
    }

    /// <summary>The developer line of the ship under the crosshair (numbers from the client's own copy: the server decides).</summary>
    private void AppendShip(AimedShip ship)
    {
        var stats = ship.Stats;
        _text.Append('\n').Append("G ").Append(T("SHIP_TOGGLE")).Append("   ").Append(T("SHIP_MODE")).Append(": ")
            .Append(T(ship.Mode == ConstructMode.Ship ? "SHIP_MODE_SHIP" : "SHIP_MODE_STATIC")).Append("   ").Append(T("SHIP_MASS")).Append(' ')
            .Append(stats.MassKg.ToString("F0", CultureInfo.InvariantCulture)).Append(" kg   ").Append(T("SHIP_THRUST")).Append(" +Y ")
            .Append(stats.Thrust.PosY.ToString("F0", CultureInfo.InvariantCulture)).Append(" N   ").Append(T("SHIP_ENGINES")).Append(' ')
            .Append(stats.Engines).Append("   ").Append(T("SHIP_SEATS")).Append(' ').Append(stats.Seats.Count);
    }

    private string T(string key)
    {
        if (!_translations.TryGetValue(key, out var text))
        {
            text = Tr(key);
            _translations[key] = text;
        }

        return text;
    }

    private string RejectText(RejectReason reason)
    {
        var index = (byte)reason;
        return _rejectTexts[index] ??= Tr(RejectKey(reason));
    }

    private string MaterialName(byte id)
    {
        var entries = _tools.Materials?.Entries;
        for (var i = 0; entries is not null && i < entries.Count; i++)
        {
            if (entries[i].Id == id)
            {
                return entries[i].Key;
            }
        }

        return id.ToString(CultureInfo.InvariantCulture);
    }

    private string BlockName(ushort id)
    {
        var entries = _tools.Blocks?.Entries;
        for (var i = 0; entries is not null && i < entries.Count; i++)
        {
            if (entries[i].Id == id)
            {
                return entries[i].Key;
            }
        }

        return id.ToString(CultureInfo.InvariantCulture);
    }

    private readonly record struct Shown(
        ToolKind Tool, float Radius, byte Material, ushort Block, byte Orientation, RejectReason Hint, bool Waiting, RejectReason Refusal,
        AimedShip? Ship, ShipModeToggleResult? Toggle, int StockCount, string StockName);
}
