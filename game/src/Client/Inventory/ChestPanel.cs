using Godot;

using Horyzont.Client.Inventory;
using Horyzont.Client.Survival;
using Horyzont.Core.Items;

namespace Horyzont.Game.Client.Inventory;

/// <summary>
/// The panel of an open chest next to the backpack on the inventory screen (K6-30): the container as a grid of eight columns, the
/// access of the chest and the buttons "Take all", "Put matching" and, for the owner, the access switch. It only shows
/// <see cref="ChestSession"/> and sends its calls; every move is an intent that the server answers. The look is built from the
/// components and the theme of the inventory screen (<see cref="HudTheme"/>, <see cref="SlotView"/>).
/// </summary>
public sealed partial class ChestPanel : PanelContainer
{
    private readonly InventoryNode _node;
    private readonly Func<Godot.Container, string, SlotView> _newSlot;
    private readonly List<SlotView> _slots = [];
    private Label? _title;
    private Label? _access;
    private Label? _time;
    private bool _generator;
    private long _shownSeconds = -1;
    private GridContainer? _grid;
    private Button? _takeAll;
    private Button? _quickStack;
    private Button? _accessSwitch;

    /// <summary>Creates the panel, hidden.</summary>
    /// <param name="node">The inventory whose chest session the panel shows.</param>
    /// <param name="newSlot">Creates a slot in a container, wired to the clicks and the tooltip of the screen.</param>
    public ChestPanel(InventoryNode node, Func<Godot.Container, string, SlotView> newSlot)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(newSlot);
        _node = node;
        _newSlot = newSlot;
        Name = "ChestPanel";
        Visible = false;
    }

    /// <summary>Gets the slots of the chest, in slot order.</summary>
    public IReadOnlyList<SlotView> Slots => _slots;

    /// <summary>Gets the text of the heading (the chest and its access).</summary>
    public string TitleText => _title?.Text ?? string.Empty;

    /// <summary>Gets the text that names the access of the open chest.</summary>
    public string AccessText => _access?.Text ?? string.Empty;

    /// <summary>Gets the line with the time the open oxygen generator still runs, empty for a chest (K6-34).</summary>
    public string TimeText => _time?.Text ?? string.Empty;

    /// <summary>Gets a value indicating whether the panel shows an oxygen generator (K6-34).</summary>
    public bool IsGenerator => _generator;

    /// <summary>Gets a value indicating whether the access switch is on offer (the player may be the owner).</summary>
    public bool AccessSwitchVisible => _accessSwitch?.Visible ?? false;

    /// <summary>Gets the text of the access switch.</summary>
    public string AccessSwitchText => _accessSwitch?.Text ?? string.Empty;

    /// <summary>Presses the "Take all" button.</summary>
    public void PressTakeAll() => _takeAll?.EmitSignal(BaseButton.SignalName.Pressed);

    /// <summary>Presses the "Put matching" button.</summary>
    public void PressQuickStack() => _quickStack?.EmitSignal(BaseButton.SignalName.Pressed);

    /// <summary>Presses the access switch.</summary>
    public void PressAccessSwitch() => _accessSwitch?.EmitSignal(BaseButton.SignalName.Pressed);

    /// <inheritdoc />
    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", HudTheme.Panel(HudTheme.Border, 1, new Color(0.05f, 0.07f, 0.1f, 0.6f), 10f));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        _title = new Label();
        HudTheme.StyleLabel(_title, 15, HudTheme.Muted);
        column.AddChild(_title);
        _access = new Label();
        HudTheme.StyleLabel(_access, 13, HudTheme.AccentTool);
        column.AddChild(_access);
        _time = new Label { Visible = false };
        HudTheme.StyleLabel(_time, 14, HudTheme.Accent);
        column.AddChild(_time);

        _grid = new GridContainer { Name = "Chest", Columns = InventoryScreen.Columns };
        _grid.AddThemeConstantOverride("h_separation", 4);
        _grid.AddThemeConstantOverride("v_separation", 4);
        column.AddChild(_grid);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        column.AddChild(buttons);
        _takeAll = NewButton(buttons, HudTheme.Accent, () => _node.Chests.TakeAll());
        _quickStack = NewButton(buttons, HudTheme.AccentTool, () => _node.Chests.QuickStack());
        _accessSwitch = NewButton(column, HudTheme.AccentTool, () => _node.Chests.ToggleAccess());
    }

    /// <summary>Shows the open chest, or hides the panel when none is open.</summary>
    /// <param name="held">The items in hand (the source slot is dimmed).</param>
    /// <param name="interaction">The mouse logic (slots in transit are dimmed).</param>
    public void Update(HeldStack? held, InventoryInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        var chest = _node.Chests.OpenContainer;
        Visible = chest is not null;
        if (chest is null || _grid is null)
        {
            return;
        }

        while (_slots.Count < chest.Size)
        {
            _slots.Add(_newSlot(_grid, $"Chest{_slots.Count}"));
        }

        for (var i = 0; i < _slots.Count; i++)
        {
            var view = _slots[i];
            var present = i < chest.Size;
            view.Visible = present;
            if (!present)
            {
                continue;
            }

            var slotRef = new SlotRef(chest.Id, (ushort)i);
            var stack = chest.Get(i);
            var visual = stack.IsEmpty ? default : _node.VisualOf(stack.Type);
            view.Ref = slotRef;
            view.Set(stack, visual, -1, selected: false, dim: interaction.IsPending(slotRef) || held?.Source == slotRef, disabled: false);
        }

        // K6-34: the input of an oxygen generator is shown like a chest, under its own heading and with the time it still runs; it has no access to switch.
        _generator = chest.Kind == ContainerKind.StationInput;
        var isPrivate = chest.Access == ContainerAccess.Private;
        _title!.Text = Tr(_generator ? "HUD_OXYGEN_GENERATOR" : "INV_CHEST");
        _access!.Visible = !_generator;
        _access.Text = _generator ? string.Empty : Tr(isPrivate ? "INV_CHEST_PRIVATE" : "INV_CHEST_SHARED");
        _takeAll!.Text = Tr("INV_TAKE_ALL");
        _quickStack!.Text = Tr("INV_QUICK_STACK");
        _quickStack.Visible = !_generator; // quick stacking sends to the chests nearby, nothing to do with a generator (review C-D, P3-11)
        _accessSwitch!.Text = Tr(isPrivate ? "INV_ACCESS_TO_SHARED" : "INV_ACCESS_TO_PRIVATE");
        _accessSwitch.Visible = !_generator && _node.Chests.CanConfigureAccess;
        UpdateTime(force: true);
    }

    /// <summary>Refreshes the time line of a generator; the time moves with the tick of the server, not with the version of the screen.</summary>
    /// <param name="force">Set the text even when the second is the same (the panel was rebuilt, the language may have changed).</param>
    public void UpdateTime(bool force = false)
    {
        if (_time is null)
        {
            return;
        }

        var chests = _node.Chests;
        if (!_generator || !Visible || _node.GeneratorTime is not { } generatorTime || !_node.TryGetServerTick(out var tick)
            || !generatorTime.TryGetRemaining(chests.Construct, chests.Cell, tick, out var ticks))
        {
            _time.Visible = false;
            _time.Text = string.Empty;
            _shownSeconds = -1;
            return;
        }

        // The text moves by whole seconds: build it only then (review C-D, P3-4).
        var rate = (ulong)Horyzont.Server.GameServerHooks.TickRate;
        var seconds = (long)((ticks / rate) + (ticks % rate == 0 ? 0UL : 1UL));
        if (!force && _time.Visible && seconds == _shownSeconds)
        {
            return;
        }

        _shownSeconds = seconds;
        _time.Text = string.Concat(Tr("HUD_GENERATOR_REMAINING"), " ", SuitHudText.GeneratorRemaining(ticks, Horyzont.Server.GameServerHooks.TickRate));
        _time.Visible = true;
    }

    private Button NewButton(Godot.Container parent, Color accent, Action pressed)
    {
        var button = new Button();
        HudTheme.StyleButton(button, accent);
        button.Pressed += () =>
        {
            pressed();
            _node.Refresh();
        };
        parent.AddChild(button);
        return button;
    }
}
