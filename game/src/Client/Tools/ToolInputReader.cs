using Godot;

using Horyzont.Client.Tools;
using Horyzont.Client.UI;

namespace Horyzont.Game.Client.Tools;

/// <summary>
/// Reads the tool actions of the <c>InputMap</c> (K2-13, table "Sterowanie"). Presses, wheel notches and key presses are counted as
/// events arrive in <c>_Input</c> and handed over in one piece by <see cref="Take"/> in the physics tick, so a high frame rate neither
/// loses nor doubles a step. Nothing is reported while the mouse is released (the click that takes the mouse back is not a dig) or
/// while <see cref="Blocked"/> says the inventory screen is open. The wheel turns the radius or the block palette only with Shift;
/// without it the wheel belongs to the hotbar (K6-29W D18). The tool itself comes from the selected hotbar slot, not from keys.
/// </summary>
public sealed partial class ToolInputReader : Node, IToolInputSource
{
    private static readonly StringName _primary = new("tool_primary");
    private static readonly StringName _secondary = new("tool_secondary");
    private static readonly StringName _next = new("tool_next");
    private static readonly StringName _prev = new("tool_prev");
    private static readonly StringName _materialNext = new("tool_material_next");
    private static readonly StringName _rotateYaw = new("tool_rotate_yaw");
    private static readonly StringName _rotateUp = new("tool_rotate_up");
    private static readonly StringName _shipMode = new("ship_mode_toggle");

    private bool _primaryPressed;
    private bool _secondaryPressed;
    private int _wheel;
    private int _material;
    private int _yaw;
    private int _up;
    private bool _shipModeToggle;

    /// <summary>Gets or sets the gate that blocks tool input while a menu or modal has focus (K3-18); null for none.</summary>
    public IGameplayInputGate? Gate { get; set; }

    /// <summary>Gets or sets a function that says the input is blocked (the inventory screen is open); null for never.</summary>
    public Func<bool>? Blocked { get; set; }

    /// <summary>Gets a value indicating whether tool input is read now: the mouse is captured or the window is headless, and nothing blocks it.</summary>
    public bool Active => !(Blocked?.Invoke() ?? false) && (Input.MouseMode == Input.MouseModeEnum.Captured || DisplayServer.GetName() == "headless");

    private bool IsReading => Active && Gate?.IsBlocked != true;

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (!IsReading)
        {
            return;
        }

        if (@event.IsActionPressed(_primary))
        {
            _primaryPressed = true;
        }
        else if (@event.IsActionPressed(_secondary))
        {
            _secondaryPressed = true;
        }
        else if (@event.IsActionPressed(_next))
        {
            if (@event is InputEventMouseButton { ShiftPressed: true })
            {
                _wheel++;
            }
        }
        else if (@event.IsActionPressed(_prev))
        {
            if (@event is InputEventMouseButton { ShiftPressed: true })
            {
                _wheel--;
            }
        }
        else if (@event.IsActionPressed(_materialNext))
        {
            _material++;
        }
        else if (@event.IsActionPressed(_rotateYaw))
        {
            _yaw++;
        }
        else if (@event.IsActionPressed(_rotateUp))
        {
            _up++;
        }
        else if (@event.IsActionPressed(_shipMode))
        {
            _shipModeToggle = true;
        }
    }

    /// <inheritdoc />
    public ToolInputFrame Take()
    {
        var active = IsReading;
        if (Gate?.IsBlocked == true)
        {
            // A menu has focus: whatever was pressed before it opened is not a tool action.
            _primaryPressed = false;
            _secondaryPressed = false;
            _wheel = 0;
            _material = 0;
            _yaw = 0;
            _up = 0;
        }

        var frame = new ToolInputFrame(
            _primaryPressed, active && Input.IsActionPressed(_primary), _secondaryPressed, active && Input.IsActionPressed(_secondary),
            null, _wheel, 0, _material, _yaw, _up, _shipModeToggle);
        _primaryPressed = false;
        _secondaryPressed = false;
        _wheel = 0;
        _material = 0;
        _yaw = 0;
        _up = 0;
        _shipModeToggle = false;
        return frame;
    }
}
