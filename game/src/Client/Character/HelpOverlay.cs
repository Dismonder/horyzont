using System.Globalization;

using Godot;

using Horyzont.Game.Client.Sky;

namespace Horyzont.Game.Client.Character;

/// <summary>
/// A minimal on-screen help for solo and host players until the real HUD (K3-18): the controls and a few lines about the state of
/// the character and the sky. Hidden with <c>F1</c>.
/// </summary>
public sealed partial class HelpOverlay : CanvasLayer
{
    private const string Controls =
        "WASD move   Mouse look   Space jump   Shift sprint   V camera (3rd/1st person)   Esc free the mouse (click to capture again)\n"
        + "Dev builds: F fly (character)   F8 free camera   F9 camera to orbit   Wheel = free camera speed   F1 hide this";

    private readonly LocalPlayer _player;
    private readonly ClientDayNight? _dayNight;
    private readonly DevFlyCamera? _dev;
    private Label? _controls;
    private Label? _status;

    /// <summary>Creates the overlay.</summary>
    /// <param name="player">The local player to report.</param>
    /// <param name="dayNight">The sky, for the time of day; null for none.</param>
    /// <param name="dev">The developer camera, for its speed; null in release builds.</param>
    public HelpOverlay(LocalPlayer player, ClientDayNight? dayNight, DevFlyCamera? dev)
    {
        ArgumentNullException.ThrowIfNull(player);
        _player = player;
        _dayNight = dayNight;
        _dev = dev;
        Name = "HelpOverlay";
        Layer = 10;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _controls = new Label { Text = Controls, Position = new Vector2(12f, 8f) };
        _status = new Label { Position = new Vector2(12f, 56f) };
        foreach (var label in new[] { _controls, _status })
        {
            label.AddThemeColorOverride("font_color", Colors.White);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 4);
            AddChild(label);
        }
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F1 })
        {
            Visible = !Visible;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!Visible || _status is null)
        {
            return;
        }

        var feet = _player.PredictedFeet;
        var altitude = _player.IsActive || _player.Body is not null ? feet.Length() : 0f;
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{Engine.GetFramesPerSecond():F0} fps   {_player.CameraMode}   server mode {_player.ServerMode}   pitch {_player.Pitch * 57.29578f:F0}   radius {altitude:F1} m"
            + $"   corrections {_player.Corrections}   camera jumps {_player.CameraJumps}");
        if (_player.PredictionPaused)
        {
            text += "\nwaiting for the server";
        }

        if (_dayNight is { } sky)
        {
            text += string.Create(CultureInfo.InvariantCulture, $"\ndaylight {sky.Daylight:P0}   tick {sky.AppliedTick}");
        }

        if (_player.DevFlyOn)
        {
            text += "\nCHARACTER FLIGHT";
        }

        if (_dev is { IsActive: true })
        {
            text += string.Create(CultureInfo.InvariantCulture, $"\nFREE CAMERA {_dev.Speed:F0} m/s (x{_dev.SpeedMultiplier:F2})");
        }

        _status.Text = text;
    }
}
