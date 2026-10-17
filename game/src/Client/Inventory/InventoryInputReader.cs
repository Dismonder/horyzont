using Godot;

namespace Horyzont.Game.Client.Inventory;

/// <summary>
/// Reads the hotbar keys 1 to 0, the mouse wheel (without Shift: with Shift the wheel belongs to the tool) and the inventory keys
/// (Tab and I) from the <c>InputMap</c>. Events are counted as they arrive and handed over by <see cref="Take"/> once per frame.
/// The wheel counts only while the mouse is captured, so it does not turn the hotbar behind the inventory screen.
/// </summary>
public sealed partial class InventoryInputReader : Node, IInventoryInputSource
{
    private static readonly StringName _toggle = new("inventory_toggle");
    private static readonly StringName _release = new("release_mouse");
    private static readonly StringName _next = new("tool_next");
    private static readonly StringName _prev = new("tool_prev");

    private static readonly StringName[] _hotbar =
    [
        new("hotbar_1"), new("hotbar_2"), new("hotbar_3"), new("hotbar_4"), new("hotbar_5"),
        new("hotbar_6"), new("hotbar_7"), new("hotbar_8"), new("hotbar_9"), new("hotbar_10"),
    ];

    private int _key = -1;
    private int _wheel;
    private bool _toggleKey;
    private bool _escape;

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event.IsEcho())
        {
            return;
        }

        if (@event.IsActionPressed(_toggle))
        {
            _toggleKey = true;
            return;
        }

        if (@event.IsActionPressed(_release))
        {
            _escape = true;
            return;
        }

        for (var i = 0; i < _hotbar.Length; i++)
        {
            if (@event.IsActionPressed(_hotbar[i]))
            {
                _key = (i + 1) % 10;
                return;
            }
        }

        var wheelAllowed = (Input.MouseMode == Input.MouseModeEnum.Captured || DisplayServer.GetName() == "headless")
            && @event is InputEventMouseButton { ShiftPressed: false };
        if (wheelAllowed && @event.IsActionPressed(_next))
        {
            _wheel--; // wheel away from the user: the previous slot
        }
        else if (wheelAllowed && @event.IsActionPressed(_prev))
        {
            _wheel++;
        }
    }

    /// <inheritdoc />
    public InventoryInputFrame Take()
    {
        var frame = new InventoryInputFrame(_key, _wheel, _toggleKey, _escape);
        _key = -1;
        _wheel = 0;
        _toggleKey = false;
        _escape = false;
        return frame;
    }
}
