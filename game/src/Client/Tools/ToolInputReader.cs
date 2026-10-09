using Godot;

using Horyzont.Client.Tools;

namespace Horyzont.Game.Client.Tools;

/// <summary>
/// Reads the tool actions of the <c>InputMap</c> (K2-13, table "Sterowanie"). Presses, wheel notches and key presses are counted as
/// events arrive in <c>_Input</c> and handed over in one piece by <see cref="Take"/> in the physics tick, so a high frame rate neither
/// loses nor doubles a step. Nothing is reported while the mouse is released (the click that takes the mouse back is not a dig).
/// </summary>
public sealed partial class ToolInputReader : Node, IToolInputSource
{
    private static readonly StringName _primary = new("tool_primary");
    private static readonly StringName _secondary = new("tool_secondary");
    private static readonly StringName _selectTerrain = new("tool_select_terrain");
    private static readonly StringName _selectBuild = new("tool_select_build");
    private static readonly StringName _next = new("tool_next");
    private static readonly StringName _prev = new("tool_prev");
    private static readonly StringName _materialNext = new("tool_material_next");
    private static readonly StringName _rotateYaw = new("tool_rotate_yaw");
    private static readonly StringName _rotateUp = new("tool_rotate_up");

    private bool _primaryPressed;
    private bool _secondaryPressed;
    private ToolKind? _select;
    private int _wheel;
    private int _material;
    private int _yaw;
    private int _up;

    /// <summary>Gets a value indicating whether tool input is read now: the mouse is captured or the window is headless.</summary>
    public static bool Active => Input.MouseMode == Input.MouseModeEnum.Captured || DisplayServer.GetName() == "headless";

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (!Active)
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
        else if (@event.IsActionPressed(_selectTerrain))
        {
            _select = ToolKind.TerrainManipulator;
        }
        else if (@event.IsActionPressed(_selectBuild))
        {
            _select = ToolKind.Builder;
        }
        else if (@event.IsActionPressed(_next))
        {
            _wheel++;
        }
        else if (@event.IsActionPressed(_prev))
        {
            _wheel--;
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
    }

    /// <inheritdoc />
    public ToolInputFrame Take()
    {
        var active = Active;
        var frame = new ToolInputFrame(
            _primaryPressed, active && Input.IsActionPressed(_primary), _secondaryPressed, active && Input.IsActionPressed(_secondary),
            _select, _wheel, 0, _material, _yaw, _up);
        _primaryPressed = false;
        _secondaryPressed = false;
        _select = null;
        _wheel = 0;
        _material = 0;
        _yaw = 0;
        _up = 0;
        return frame;
    }
}
