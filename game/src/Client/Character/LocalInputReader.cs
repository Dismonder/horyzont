using Godot;

using Horyzont.Client.UI;
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

    private const ulong SettleMilliseconds = 400;
    private const float MaxEventPixels = 400f;

    private static readonly StringName _release = new("release_mouse");
    private static readonly StringName _jump = new("jump");
    private static readonly StringName _sprint = new("sprint");
    private static readonly StringName _crouch = new("crouch");
    private static readonly StringName _left = new("move_left");
    private static readonly StringName _right = new("move_right");
    private static readonly StringName _back = new("move_back");
    private static readonly StringName _forward = new("move_forward");

    private ulong _ignoreMotionUntil;
    private float _yaw;
    private float _pitch;
    private int _wheel;

    /// <summary>Gets or sets the gate that blocks all gameplay input while a menu or modal has focus (K3-18); null for none.</summary>
    public IGameplayInputGate? Gate { get; set; }

    /// <summary>Gets or sets the mouse sensitivity in radians per pixel.</summary>
    public float Sensitivity { get; set; } = RadiansPerPixel;

    /// <summary>Gets a value indicating whether the mouse is captured by the game.</summary>
    public static bool MouseCaptured => Input.MouseMode == Input.MouseModeEnum.Captured;

    /// <inheritdoc />
    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless" && Gate?.IsBlocked != true)
        {
            Capture();
        }
    }

    /// <summary>Takes the mouse again after a menu or modal closed.</summary>
    public void Recapture()
    {
        if (DisplayServer.GetName() != "headless")
        {
            Capture();
        }
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (Gate?.IsBlocked == true)
        {
            return;
        }

        switch (@event)
        {
            case InputEventMouseMotion motion when MouseCaptured && Time.GetTicksMsec() >= _ignoreMotionUntil
                && motion.Relative.LengthSquared() < MaxEventPixels * MaxEventPixels:
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
        if (Gate?.IsBlocked == true)
        {
            return;
        }

        if (@event.IsActionPressed(_release) && MouseCaptured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && !MouseCaptured)
        {
            Capture();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Capture()
    {
        // Capturing moves the pointer to the window centre; the first motion events can carry that jump as a huge turn.
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _ignoreMotionUntil = Time.GetTicksMsec() + SettleMilliseconds;
        _yaw = 0f;
        _pitch = 0f;
    }

    /// <inheritdoc />
    public LocalInputSample Take()
    {
        if (Gate?.IsBlocked == true)
        {
            _yaw = 0f;
            _pitch = 0f;
            _wheel = 0;
            return LocalInputSample.None;
        }

        var buttons = InputButtons.None;
        if (Input.IsActionPressed(_jump))
        {
            buttons |= InputButtons.Jump;
        }

        if (Input.IsActionPressed(_sprint))
        {
            buttons |= InputButtons.Sprint;
        }

        if (Input.IsActionPressed(_crouch))
        {
            buttons |= InputButtons.Crouch;
        }

        var sample = new LocalInputSample(
            Input.GetAxis(_left, _right), Input.GetAxis(_back, _forward), buttons, _yaw, _pitch, _wheel);
        _yaw = 0f;
        _pitch = 0f;
        _wheel = 0;
        return sample;
    }
}
