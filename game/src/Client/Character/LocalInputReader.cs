using Godot;

using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Game.Client.Character;

/// <summary>
/// Reads the keyboard and the mouse (the <c>InputMap</c> actions of <c>project.godot</c>). The mouse is captured while the game runs;
/// <c>Esc</c> releases it and a click takes it again.
/// </summary>
public sealed partial class LocalInputReader : Node, ILocalInputSource
{
    /// <summary>Radians of turn per pixel of mouse movement.</summary>
    public const float RadiansPerPixel = 0.0025f;

    private float _yaw;
    private float _pitch;
    private int _wheel;

    /// <summary>Gets or sets the mouse sensitivity in radians per pixel.</summary>
    public float Sensitivity { get; set; } = RadiansPerPixel;

    /// <summary>Gets a value indicating whether the mouse is captured by the game.</summary>
    public static bool MouseCaptured => Input.MouseMode == Input.MouseModeEnum.Captured;

    /// <inheritdoc />
    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion when MouseCaptured:
                _yaw -= (float)motion.Relative.X * Sensitivity;
                _pitch -= (float)motion.Relative.Y * Sensitivity;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _wheel++;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _wheel--;
                break;
            default:
                break;
        }
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("release_mouse") && MouseCaptured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && !MouseCaptured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public LocalInputSample Take()
    {
        var buttons = InputButtons.None;
        if (Input.IsActionPressed("jump"))
        {
            buttons |= InputButtons.Jump;
        }

        if (Input.IsActionPressed("sprint"))
        {
            buttons |= InputButtons.Sprint;
        }

        if (Input.IsActionPressed("crouch"))
        {
            buttons |= InputButtons.Crouch;
        }

        var sample = new LocalInputSample(
            Input.GetAxis("move_left", "move_right"), Input.GetAxis("move_back", "move_forward"), buttons, _yaw, _pitch, _wheel);
        _yaw = 0f;
        _pitch = 0f;
        _wheel = 0;
        return sample;
    }
}
