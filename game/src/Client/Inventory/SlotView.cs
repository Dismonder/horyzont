using System.Globalization;

using Godot;

using Horyzont.Core.Items;

namespace Horyzont.Game.Client.Inventory;

/// <summary>One slot of the hotbar or of the inventory screen: panel, icon, count in the lower right corner, optional number.</summary>
public sealed partial class SlotView : Control
{
    /// <summary>Side of a slot in pixels.</summary>
    public const float Side = 56f;

    private StyleBoxFlat? _normal;
    private StyleBoxFlat? _selected;
    private StyleBoxFlat? _hover;
    private ItemStack _stack;
    private ItemVisual _visual;
    private int _number = -1;
    private bool _isSelected;
    private bool _isHover;
    private bool _isDim;
    private bool _isDisabled;

    /// <summary>Raised when the player pressed a mouse button on the slot (button, shift held).</summary>
    public event Action<SlotView, MouseButton, bool>? Clicked;

    /// <summary>Raised when the cursor enters (true) or leaves (false) the slot.</summary>
    public event Action<SlotView, bool>? HoverChanged;

    /// <summary>Gets or sets the slot this view shows; meaningless for a slot without a container (equipment placeholder).</summary>
    public SlotRef Ref { get; set; }

    /// <summary>Gets the stack on show.</summary>
    public ItemStack Stack => _stack;

    /// <summary>Gets a value indicating whether the slot is highlighted as the selected hotbar slot.</summary>
    public bool IsSelected => _isSelected;

    /// <summary>Gets the number on the slot (1 to 9, 0), or -1 for none.</summary>
    public int Number => _number;

    /// <inheritdoc />
    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(Side, Side);
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () =>
        {
            _isHover = !_isDisabled;
            QueueRedraw();
            HoverChanged?.Invoke(this, true);
        };
        MouseExited += () =>
        {
            _isHover = false;
            QueueRedraw();
            HoverChanged?.Invoke(this, false);
        };
    }

    /// <summary>Sets what the slot shows; redraws only on a change.</summary>
    /// <param name="stack">The stack.</param>
    /// <param name="visual">Its visual.</param>
    /// <param name="number">Number label, -1 for none.</param>
    /// <param name="selected">Whether the slot is the selected hotbar slot.</param>
    /// <param name="dim">Whether the slot is in transit or its stack is in hand.</param>
    /// <param name="disabled">Whether the slot is a greyed placeholder.</param>
    public void Set(ItemStack stack, ItemVisual visual, int number, bool selected, bool dim, bool disabled)
    {
        if (stack == _stack && visual == _visual && number == _number && selected == _isSelected && dim == _isDim && disabled == _isDisabled)
        {
            return;
        }

        _stack = stack;
        _visual = visual;
        _number = number;
        _isSelected = selected;
        _isDim = dim;
        _isDisabled = disabled;
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } click && !_isDisabled
            && click.ButtonIndex is MouseButton.Left or MouseButton.Right)
        {
            AcceptEvent();
            Clicked?.Invoke(this, click.ButtonIndex, click.ShiftPressed);
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        _normal ??= HudTheme.Panel(HudTheme.Border, 1, HudTheme.SlotBackground, 0f);
        _selected ??= HudTheme.Panel(HudTheme.Accent, 2, new Color(HudTheme.SlotBackground, 0.95f), 0f);
        _hover ??= HudTheme.Panel(HudTheme.AccentTool, 1, new Color(0.12f, 0.17f, 0.22f, 0.92f), 0f);
        var rect = new Rect2(Vector2.Zero, Size);
        DrawStyleBox(_isSelected ? _selected : _isHover ? _hover : _normal, rect);
        var alpha = _isDisabled ? 0.25f : _isDim ? 0.4f : 1f;
        var font = ThemeDB.FallbackFont;
        if (!_stack.IsEmpty)
        {
            ItemIconPainter.Draw(this, new Rect2(8f, 8f, Size.X - 16f, Size.Y - 16f), _visual, alpha);
            if (_stack.Count > 1)
            {
                var text = _stack.Count.ToString(CultureInfo.InvariantCulture);
                var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, 14).X;
                var at = new Vector2(Size.X - width - 5f, Size.Y - 5f);
                DrawString(font, at + new Vector2(1f, 1f), text, HorizontalAlignment.Left, -1, 14, new Color(0f, 0f, 0f, 0.8f));
                DrawString(font, at, text, HorizontalAlignment.Left, -1, 14, HudTheme.Text);
            }
        }

        if (_number >= 0)
        {
            DrawString(
                font, new Vector2(5f, 14f), _number.ToString(CultureInfo.InvariantCulture), HorizontalAlignment.Left, -1, 12,
                _isSelected ? HudTheme.Accent : HudTheme.Muted);
        }
    }
}
