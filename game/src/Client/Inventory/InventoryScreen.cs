using System.Globalization;

using Godot;

using Horyzont.Client.Inventory;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;

using ToolHud = Horyzont.Game.Client.Tools.ToolHud;

namespace Horyzont.Game.Client.Inventory;

/// <summary>
/// The inventory screen (Tab or I): the 8 by 5 backpack grid, the hotbar under it and a greyed "coming soon" equipment panel. It is a
/// full-screen layer that takes every click, so the game below sees neither the mouse nor the tools while it is open.
/// </summary>
public sealed partial class InventoryScreen : CanvasLayer
{
    /// <summary>Columns of the backpack grid.</summary>
    public const int Columns = 8;

    private const int EquipmentSlots = 7;

    private readonly InventoryNode _node;
    private readonly List<SlotView> _bag = [];
    private readonly List<SlotView> _hotbar = [];
    private readonly List<SlotView> _equipment = [];
    private readonly ItemTooltip _tooltip = new();
    private Label? _title;
    private Label? _bagTitle;
    private Label? _hotbarTitle;
    private Label? _equipmentTitle;
    private Label? _soon;
    private Label? _help;
    private Label? _refusal;
    private Button? _discard;
    private Button? _quickStack;
    private ChestPanel? _chestPanel;
    private CraftingPanel? _craftingPanel;
    private StationPanel? _stationPanel;
    private PanelContainer? _confirm;
    private Label? _confirmText;
    private Button? _yes;
    private Button? _no;
    private SlotView? _handView;
    private SlotView? _hover;
    private Control? _root;
    private uint _shownVersion = uint.MaxValue;
    private SlotView? _tipView;
    private ItemStack _tipStack;

    /// <summary>Creates the screen, closed.</summary>
    /// <param name="node">The inventory it shows.</param>
    public InventoryScreen(InventoryNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _node = node;
        Name = "InventoryScreen";
        Layer = 20;
        Visible = false;
    }

    /// <summary>Gets the slots of the backpack grid, in slot order.</summary>
    public IReadOnlyList<SlotView> BagSlots => _bag;

    /// <summary>Gets the slots of the hotbar row, in slot order.</summary>
    public IReadOnlyList<SlotView> HotbarSlots => _hotbar;

    /// <summary>Gets the greyed equipment placeholders.</summary>
    public IReadOnlyList<SlotView> EquipmentViews => _equipment;

    /// <summary>Gets the panel of the open chest; null before the screen entered the tree.</summary>
    public ChestPanel? Chest => _chestPanel;

    /// <summary>Gets the crafting panel (K6-32); null before the screen entered the tree.</summary>
    public CraftingPanel? Crafting => _craftingPanel;

    /// <summary>Gets the panel of the open furnace or refinery (K6-32); null before the screen entered the tree.</summary>
    public StationPanel? Station => _stationPanel;

    /// <summary>Gets the button that puts the matching items into the chests nearby (shown while no chest panel is open).</summary>
    public Button? QuickStackButton => _quickStack;

    /// <summary>Gets the tooltip.</summary>
    public ItemTooltip Tooltip => _tooltip;

    /// <summary>Gets a value indicating whether the discard question is on show.</summary>
    public bool ConfirmVisible => _confirm?.Visible ?? false;

    /// <summary>Gets the text under the screen that names the latest refusal of the server.</summary>
    public string RefusalText => _refusal?.Text ?? string.Empty;

    /// <summary>Shows the tooltip of a slot as if the cursor rested on it (tests and keyboard use).</summary>
    /// <param name="view">The slot.</param>
    public void HoverSlot(SlotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _hover = view;
        UpdateTooltip(Vector2.Zero, force: true);
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var dim = new ColorRect { Name = "Dim", Color = new Color(0f, 0f, 0f, 0.45f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        var center = new CenterContainer { Name = "Center", MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        var main = new PanelContainer { Name = "Panel" };
        main.AddThemeStyleboxOverride("panel", HudTheme.Panel(HudTheme.Border, 1, null, 16f));
        center.AddChild(main);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        main.AddChild(column);

        _title = new Label();
        HudTheme.StyleLabel(_title, 22, HudTheme.Accent);
        column.AddChild(_title);

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 24);
        column.AddChild(body);

        // The chest panel stands next to the backpack, so a large chest does not push the backpack off the screen.
        _chestPanel = new ChestPanel(_node, NewSlot);
        body.AddChild(_chestPanel);

        // The same place serves a station with time (K6-32) and the crafting list: only one of the three shows at a time.
        _stationPanel = new StationPanel(_node, NewSlot);
        body.AddChild(_stationPanel);
        _craftingPanel = new CraftingPanel(_node);
        body.AddChild(_craftingPanel);

        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 8);
        body.AddChild(left);
        _bagTitle = new Label();
        HudTheme.StyleLabel(_bagTitle, 15, HudTheme.Muted);
        left.AddChild(_bagTitle);
        var grid = new GridContainer { Name = "Bag", Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        left.AddChild(grid);
        for (var i = 0; i < 40; i++)
        {
            _bag.Add(NewSlot(grid, $"Bag{i}"));
        }

        _hotbarTitle = new Label();
        HudTheme.StyleLabel(_hotbarTitle, 15, HudTheme.Muted);
        left.AddChild(_hotbarTitle);
        var row = new HBoxContainer { Name = "Hotbar" };
        row.AddThemeConstantOverride("separation", 4);
        left.AddChild(row);
        for (var i = 0; i < HotbarSelection.SlotCount; i++)
        {
            _hotbar.Add(NewSlot(row, $"Hot{i}"));
        }

        var equipment = new PanelContainer { Name = "Equipment" };
        equipment.AddThemeStyleboxOverride("panel", HudTheme.Panel(HudTheme.Border, 1, new Color(0.05f, 0.07f, 0.1f, 0.6f), 10f));
        body.AddChild(equipment);
        var equipmentBox = new VBoxContainer();
        equipmentBox.AddThemeConstantOverride("separation", 8);
        equipment.AddChild(equipmentBox);
        _equipmentTitle = new Label();
        HudTheme.StyleLabel(_equipmentTitle, 15, HudTheme.Muted);
        equipmentBox.AddChild(_equipmentTitle);
        var equipmentGrid = new GridContainer { Columns = 2 };
        equipmentGrid.AddThemeConstantOverride("h_separation", 4);
        equipmentGrid.AddThemeConstantOverride("v_separation", 4);
        equipmentBox.AddChild(equipmentGrid);
        for (var i = 0; i < EquipmentSlots; i++)
        {
            var slot = NewSlot(equipmentGrid, $"Eq{i}");
            slot.Set(ItemStack.Empty, default, -1, selected: false, dim: false, disabled: true);
            _equipment.Add(slot);
        }

        _soon = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        HudTheme.StyleLabel(_soon, 15, HudTheme.AccentTool);
        equipmentBox.AddChild(_soon);

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 16);
        column.AddChild(footer);
        _help = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        HudTheme.StyleLabel(_help, 13, HudTheme.Muted);
        footer.AddChild(_help);
        _quickStack = new Button { Name = "QuickStack" };
        HudTheme.StyleButton(_quickStack, HudTheme.AccentTool);
        _quickStack.Pressed += () =>
        {
            _node.Chests.QuickStack();
            _node.Refresh();
        };
        footer.AddChild(_quickStack);
        _discard = new Button();
        HudTheme.StyleButton(_discard, HudTheme.Danger);
        _discard.Pressed += () =>
        {
            _node.Interaction.Discard();
            _node.Refresh();
        };
        footer.AddChild(_discard);
        _refusal = new Label();
        HudTheme.StyleLabel(_refusal, 14, HudTheme.Danger);
        column.AddChild(_refusal);

        _confirm = new PanelContainer { Name = "Confirm", Visible = false };
        _confirm.AddThemeStyleboxOverride("panel", HudTheme.Panel(HudTheme.Danger, 2, new Color(0.04f, 0.06f, 0.09f, 0.97f), 16f));
        _confirm.SetAnchorsPreset(Control.LayoutPreset.Center);
        _confirm.GrowHorizontal = Control.GrowDirection.Both;
        _confirm.GrowVertical = Control.GrowDirection.Both;
        var confirmBox = new VBoxContainer();
        confirmBox.AddThemeConstantOverride("separation", 12);
        _confirmText = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        HudTheme.StyleLabel(_confirmText, 17);
        confirmBox.AddChild(_confirmText);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 16);
        _yes = new Button();
        HudTheme.StyleButton(_yes, HudTheme.Danger);
        _yes.Pressed += () =>
        {
            _node.Interaction.ConfirmDiscard();
            _node.Refresh();
        };
        _no = new Button();
        HudTheme.StyleButton(_no, HudTheme.AccentTool);
        _no.Pressed += () =>
        {
            _node.Interaction.CancelDiscard();
            _node.Refresh();
        };
        buttons.AddChild(_yes);
        buttons.AddChild(_no);
        confirmBox.AddChild(buttons);
        _confirm.AddChild(confirmBox);
        _root.AddChild(_confirm);

        _handView = new SlotView { Name = "Hand" };
        _root.AddChild(_handView);
        _handView.MouseFilter = Control.MouseFilterEnum.Ignore;
        _handView.Visible = false;
        _root.AddChild(_tooltip);

        _node.OpenChanged += OnOpenChanged;
        Rebuild();
    }

    /// <inheritdoc />
    public override void _ExitTree() => _node.OpenChanged -= OnOpenChanged;

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        if (_shownVersion != _node.UiVersion)
        {
            Rebuild();
        }

        var mouse = GetViewport().GetMousePosition();
        if (_handView is { Visible: true })
        {
            _handView.Position = mouse - new Vector2(SlotView.Side / 2f, SlotView.Side / 2f);
        }

        UpdateTooltip(mouse, force: false);
    }

    private void OnOpenChanged()
    {
        Visible = _node.IsOpen;
        if (!Visible)
        {
            _hover = null;
            _tooltip.HideTip();
        }
        else
        {
            Rebuild();
        }
    }

    private SlotView NewSlot(Godot.Container parent, string name)
    {
        var slot = new SlotView { Name = name };
        parent.AddChild(slot);
        slot.Clicked += (view, button, shift) => _node.OnSlotClicked(view.Ref, button, shift);
        slot.HoverChanged += (view, entered) =>
        {
            if (entered)
            {
                _hover = view;
            }
            else if (_hover == view)
            {
                _hover = null;
            }
        };
        return slot;
    }

    private void UpdateTooltip(Vector2 mouse, bool force)
    {
        if (_hover is not { } view || view.Stack.IsEmpty || _node.Interaction.Held is not null)
        {
            _tooltip.HideTip();
            _tipView = null;
            return;
        }

        if (force || _tipView != view || _tipStack != view.Stack || !_tooltip.Visible)
        {
            _tipView = view;
            _tipStack = view.Stack;
            _tooltip.ShowFor(_node, view.Stack, mouse);
        }
        else
        {
            _tooltip.Follow(mouse);
        }
    }

    private void Rebuild()
    {
        _shownVersion = _node.UiVersion;
        var interaction = _node.Interaction;
        interaction.ValidateHeld();
        var held = interaction.Held;
        FillSlots(_bag, _node.Inventory.Bag, held, interaction, numbered: false);
        FillSlots(_hotbar, _node.Inventory.Hotbar, held, interaction, numbered: true);

        _title!.Text = Tr("INV_TITLE");
        _bagTitle!.Text = Tr("INV_BAG");
        _hotbarTitle!.Text = Tr("INV_HOTBAR");
        _equipmentTitle!.Text = Tr("INV_EQUIPMENT");
        _soon!.Text = Tr("INV_SOON");
        _help!.Text = Tr("INV_HELP");
        _discard!.Text = Tr("INV_DISCARD");
        _discard.Visible = interaction.CanDiscard && !interaction.AwaitingDiscardConfirmation;
        _chestPanel!.Update(held, interaction);
        _stationPanel!.Update(held, interaction);
        _craftingPanel!.Update();
        _quickStack!.Text = Tr("INV_QUICK_STACK_NEARBY");
        _quickStack.Visible = !_node.Chests.IsOpen && !_node.Stations.IsOpen;
        _yes!.Text = Tr("INV_YES");
        _no!.Text = Tr("INV_NO");

        if (interaction.AwaitingDiscardConfirmation && held is { } confirming)
        {
            _confirmText!.Text = string.Format(CultureInfo.InvariantCulture, Tr("INV_DISCARD_CONFIRM"), _node.ItemName(confirming.Type), confirming.Count);
            _confirm!.Visible = true;
        }
        else
        {
            _confirm!.Visible = false;
        }

        var refusal = _node.Chests.LastRefusal != RejectReason.None ? _node.Chests.LastRefusal : interaction.LastRefusal;
        if (refusal == RejectReason.None && _node.Stations.IsOpen)
        {
            refusal = _node.Stations.LastRefusal;
        }

        _refusal!.Text = refusal == RejectReason.None ? string.Empty : Tr(ToolHud.RejectKey(refusal));

        if (held is { } stack)
        {
            _handView!.Set(new ItemStack(stack.Type, stack.Count), _node.VisualOf(stack.Type), -1, selected: false, dim: false, disabled: false);
            _handView.Visible = true;
        }
        else
        {
            _handView!.Visible = false;
        }
    }

    private void FillSlots(List<SlotView> views, ClientContainer? container, HeldStack? held, InventoryInteraction interaction, bool numbered)
    {
        for (var i = 0; i < views.Count; i++)
        {
            var slotRef = container is null ? default : new SlotRef(container.Id, (ushort)i);
            var stack = container?.Get(i) ?? ItemStack.Empty;
            var visual = stack.IsEmpty ? default : _node.VisualOf(stack.Type);
            var dim = container is not null && (interaction.IsPending(slotRef) || held?.Source == slotRef);
            views[i].Ref = slotRef;
            views[i].Set(stack, visual, numbered ? (i + 1) % 10 : -1, numbered && i == _node.Selection.Selected, dim, disabled: container is null);
        }
    }
}
