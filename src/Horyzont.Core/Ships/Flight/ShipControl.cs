namespace Horyzont.Core.Ships.Flight;

/// <summary>
/// Pilot control as data (K5-24 decision 1). Axes in the control frame of the cockpit: +X right, +Y up, −Z forward. An axis value
/// is the field / 127 clipped to [−1, 1]. Rotations are right-handed about the control axes: pitch about X, yaw about Y, roll
/// about Z. K5-25 sends the record 1:1 in <c>ShipInputBatch</c>; a change is a contract change.
/// </summary>
/// <param name="ThrustX">Thrust along +X (right).</param>
/// <param name="ThrustY">Thrust along +Y (up).</param>
/// <param name="ThrustZ">Thrust along +Z (backward; forward is negative).</param>
/// <param name="Pitch">Rotation about X.</param>
/// <param name="Yaw">Rotation about Y.</param>
/// <param name="Roll">Rotation about Z.</param>
/// <param name="Buttons">Button states.</param>
public readonly record struct ShipControl(sbyte ThrustX, sbyte ThrustY, sbyte ThrustZ, sbyte Pitch, sbyte Yaw, sbyte Roll, ShipButtons Buttons)
{
    /// <summary>Converts an axis field to [−1, 1].</summary>
    /// <param name="value">The field.</param>
    /// <returns>The axis value.</returns>
    public static float Axis(sbyte value) => value <= -127 ? -1f : value / 127f;
}
