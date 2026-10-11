using Horyzont.Core.Ships.Flight;

namespace Horyzont.Client.Ships;

/// <summary>
/// Turns the pilot keys and the mouse into a <see cref="ShipControl"/> (K5-25 decision 23, P8): W/S forward–back, A/D right–left,
/// Space/C up–down, Q/E roll, mouse pitch and yaw; P, L and Z toggle the pulse drive, the landing gear and the assist (the buttons are
/// states, K5-24 P9). Boarding starts with the assist on and the gear down (a parked ship). Axes are quantized to −127..127.
/// </summary>
public sealed class ShipPilotInput
{
    /// <summary>Mouse pixels per tick that give a full pitch or yaw axis.</summary>
    public const float MousePixelsForFullAxis = 12f;

    /// <summary>Gets the button states.</summary>
    public ShipButtons Buttons { get; private set; } = ShipButtons.Assist | ShipButtons.LandingGear;

    /// <summary>Back to the boarding state.</summary>
    public void Reset() => Buttons = ShipButtons.Assist | ShipButtons.LandingGear;

    /// <summary>The control of this tick.</summary>
    /// <param name="right">Right minus left key, −1..1.</param>
    /// <param name="up">Up minus down key, −1..1.</param>
    /// <param name="forward">Forward minus back key, −1..1.</param>
    /// <param name="roll">Roll right minus left key, −1..1.</param>
    /// <param name="mouseX">Mouse motion this tick, pixels (right positive).</param>
    /// <param name="mouseY">Mouse motion this tick, pixels (down positive).</param>
    /// <param name="togglePulse">The pulse key went down.</param>
    /// <param name="toggleGear">The gear key went down.</param>
    /// <param name="toggleAssist">The assist key went down.</param>
    /// <returns>The control.</returns>
    public ShipControl Build(float right, float up, float forward, float roll, float mouseX, float mouseY, bool togglePulse, bool toggleGear, bool toggleAssist)
    {
        if (togglePulse)
        {
            Buttons ^= ShipButtons.PulseDrive;
        }

        if (toggleGear)
        {
            Buttons ^= ShipButtons.LandingGear;
        }

        if (toggleAssist)
        {
            Buttons ^= ShipButtons.Assist;
        }

        // Cockpit axes: +X right, +Y up, −Z forward; pitch up for the mouse moving up, yaw right for the mouse moving right.
        return new ShipControl(
            Axis(right), Axis(up), Axis(-forward), Axis(-mouseY / MousePixelsForFullAxis), Axis(-mouseX / MousePixelsForFullAxis), Axis(-roll), Buttons);
    }

    private static sbyte Axis(float value) => float.IsFinite(value) ? (sbyte)MathF.Round(Math.Clamp(value, -1f, 1f) * 127f) : (sbyte)0;
}
