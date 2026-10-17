using Godot;

using Horyzont.Client.Inventory;
using Horyzont.Core.Items;

namespace Horyzont.Game.Client.Inventory;

/// <summary>The hotbar at the bottom of the screen: ten slots of 56 px with the numbers 1 to 0, the selected one in amber, and its name above.</summary>
public sealed partial class HotbarHud : CanvasLayer
{
    private readonly InventoryNode _node;
    private readonly SlotView[] _slots = new SlotView[HotbarSelection.SlotCount];
    private Label? _name;
    private uint _shownVersion = uint.MaxValue;

    /// <summary>Creates the hotbar.</summary>
    /// <param name="node">The inventory it shows.</param>
    public HotbarHud(InventoryNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _node = node;
        Name = "HotbarHud";
        Layer = 8;
    }

    /// <summary>Gets the slot views, in hotbar order.</summary>
    public IReadOnlyList<SlotView> Slots => _slots;

    /// <summary>Gets the text above the hotbar (the name of the selected item).</summary>
    public string SelectedName => _name?.Text ?? string.Empty;

    /// <inheritdoc />
    public override void _Ready()
    {
        var root = new VBoxContainer { Name = "Box", MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        root.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        root.OffsetBottom = -14f;
        root.GrowVertical = Control.GrowDirection.Begin;
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        _name = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        HudTheme.StyleLabel(_name, 16, HudTheme.Accent);
        root.AddChild(_name);

        var row = new HBoxContainer { Name = "Slots", Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 4);
        root.AddChild(row);
        for (var i = 0; i < _slots.Length; i++)
        {
            // Hotbar slots take no clicks: the screen handles them. The hotbar stays out of the way of the mouse.
            _slots[i] = new SlotView { Name = $"Slot{i}", MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddChild(_slots[i]);
        }

        Rebuild();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_shownVersion != _node.UiVersion)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _shownVersion = _node.UiVersion;
        Visible = !_node.IsOpen; // the screen shows its own hotbar row
        var hotbar = _node.Inventory.Hotbar;
        var selected = _node.Selection.Selected;
        for (var i = 0; i < _slots.Length; i++)
        {
            var stack = hotbar?.Get(i) ?? ItemStack.Empty;
            var visual = stack.IsEmpty ? default : _node.VisualOf(stack.Type);
            _slots[i].Set(stack, visual, (i + 1) % 10, i == selected, dim: false, disabled: false);
        }

        if (_name is not null)
        {
            var stack = _node.Selection.SelectedStack(_node.Inventory);
            _name.Text = stack.IsEmpty ? string.Empty : _node.ItemName(stack.Type);
        }
    }
}
