using System.Globalization;

using Godot;

using Horyzont.Client.Inventory;

namespace Horyzont.Game.Client.Inventory;

/// <summary>The toast column on the right of the screen: "+12 Stone" for a pickup, "Inventory full" for a dig whose yield did not fit.</summary>
public sealed partial class PickupToasts : CanvasLayer
{
    private const float FadeMs = 500f;

    private readonly InventoryNode _node;
    private readonly List<(PanelContainer Panel, PickupToast Toast)> _shown = [];
    private VBoxContainer? _column;

    /// <summary>Creates the toast column.</summary>
    /// <param name="node">The inventory whose feed it shows.</param>
    public PickupToasts(InventoryNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _node = node;
        Name = "PickupToasts";
        Layer = 10;
    }

    /// <summary>Gets the texts of the toasts on show, oldest first (diagnostics and tests).</summary>
    public IReadOnlyList<string> Texts => [.. _shown.Select(s => TextOf(s.Toast))];

    /// <inheritdoc />
    public override void _Ready()
    {
        _column = new VBoxContainer { Name = "Column", MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        _column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterRight, Control.LayoutPresetMode.Minsize, 24);
        _column.GrowHorizontal = Control.GrowDirection.Begin;
        _column.GrowVertical = Control.GrowDirection.Both;
        _column.AddThemeConstantOverride("separation", 6);
        AddChild(_column);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_column is null)
        {
            return;
        }

        var now = _node.Clock();
        var active = _node.Feed.Active;
        var same = active.Count == _shown.Count;
        for (var i = 0; same && i < active.Count; i++)
        {
            same = active[i] == _shown[i].Toast;
        }

        if (!same)
        {
            foreach (var (panel, _) in _shown)
            {
                panel.QueueFree();
            }

            _shown.Clear();
            foreach (var toast in active)
            {
                _shown.Add((Build(toast), toast));
            }
        }

        foreach (var (panel, toast) in _shown)
        {
            panel.Modulate = new Color(1f, 1f, 1f, Math.Clamp((toast.ExpiresMs - now) / FadeMs, 0f, 1f));
        }
    }

    private string TextOf(PickupToast toast) => toast.Kind == PickupToastKind.InventoryFull
        ? Tr("INV_FULL")
        : "+" + toast.Amount.ToString(CultureInfo.InvariantCulture) + " " + _node.ItemName(toast.Type);

    private PanelContainer Build(PickupToast toast)
    {
        var warning = toast.Kind == PickupToastKind.InventoryFull;
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", HudTheme.Panel(warning ? HudTheme.Danger : HudTheme.Border, 1, null, 8f));
        var label = new Label { Text = TextOf(toast), MouseFilter = Control.MouseFilterEnum.Ignore };
        HudTheme.StyleLabel(label, 17, warning ? HudTheme.Danger : HudTheme.Text);
        panel.AddChild(label);
        _column!.AddChild(panel);
        return panel;
    }
}
