using Horyzont.Core.Frames;

namespace Horyzont.Core.Ships.Flight;

/// <summary>
/// Pure helpers that turn a wanted velocity or direction into <see cref="ShipControl"/> axes; used by the autoland and the
/// development transit pilot. Allocation free.
/// </summary>
public static class ShipManeuver
{
    /// <summary>Gain of the velocity controller, 1/s.</summary>
    public const double VelocityGain = 2;

    /// <summary>Gain of the turn controller: wanted turn rate per radian of error, 1/s.</summary>
    public const double TurnGain = 2.5;

    /// <summary>Gets the rotation from the control axes to the parent frame.</summary>
    /// <param name="motion">The motion.</param>
    /// <param name="stats">Ship stats.</param>
    /// <returns>The rotation.</returns>
    public static QuaternionD ControlAxes(in ShipMotion motion, ShipStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return QuaternionD.Normalize(motion.Pose.Rotation * QuaternionD.Normalize(QuaternionD.FromFloat(stats.ControlFrame)));
    }

    /// <summary>Thrust axes that drive the velocity toward <paramref name="wanted"/> and compensate gravity, in any orientation.</summary>
    /// <param name="motion">The motion.</param>
    /// <param name="stats">Ship stats.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="wanted">Wanted velocity in the parent frame.</param>
    /// <param name="x">Receives the X axis.</param>
    /// <param name="y">Receives the Y axis.</param>
    /// <param name="z">Receives the Z axis.</param>
    public static void ThrustFor(in ShipMotion motion, ShipStats stats, in FlightEnvironment environment, in Vector3D wanted, out sbyte x, out sbyte y, out sbyte z)
    {
        ArgumentNullException.ThrowIfNull(stats);
        var axes = ControlAxes(motion, stats);
        var controlFrame = QuaternionD.Normalize(QuaternionD.FromFloat(stats.ControlFrame));
        var v = Vector3D.FromFloat(motion.LinearVelocity);
        var acceleration = ((wanted - v) * VelocityGain) - environment.Gravity + (v * environment.DragPerSecond);
        var force = acceleration * stats.MassKg;
        x = Axis(stats, controlFrame, axes, new Vector3D(1, 0, 0), force);
        y = Axis(stats, controlFrame, axes, new Vector3D(0, 1, 0), force);
        z = Axis(stats, controlFrame, axes, new Vector3D(0, 0, 1), force);
    }

    /// <summary>Turn axes that bring the control axis <paramref name="controlAxis"/> onto <paramref name="direction"/>.</summary>
    /// <param name="motion">The motion.</param>
    /// <param name="stats">Ship stats.</param>
    /// <param name="rules">Rules (turn rate).</param>
    /// <param name="controlAxis">Unit axis in control axes (for example −Z, the nose).</param>
    /// <param name="direction">Unit wanted direction in the parent frame.</param>
    /// <param name="pitch">Receives the rotation about X.</param>
    /// <param name="yaw">Receives the rotation about Y.</param>
    /// <param name="roll">Receives the rotation about Z.</param>
    /// <returns>The cosine between the axis and the direction.</returns>
    public static double TurnToward(
        in ShipMotion motion, ShipStats stats, FlightRules rules, in Vector3D controlAxis, in Vector3D direction, out sbyte pitch, out sbyte yaw, out sbyte roll)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var axes = ControlAxes(motion, stats);
        var current = axes.Rotate(controlAxis);
        var cosine = Vector3D.Dot(current, direction);
        var error = QuaternionD.Conjugate(axes).Rotate(Vector3D.Cross(current, direction));
        if (cosine < 0)
        {
            // Beyond 90 degrees the cross product shrinks: turn at full rate about the same axis.
            var length = error.Length();
            error = length > 1e-9 ? error / length : PerpendicularTo(controlAxis);
        }

        pitch = Rate(error.X, rules);
        yaw = Rate(error.Y, rules);
        roll = Rate(error.Z, rules);
        return cosine;
    }

    private static Vector3D PerpendicularTo(in Vector3D axis) => Math.Abs(axis.X) < 0.9 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);

    private static sbyte Rate(double error, FlightRules rules) => ToAxis(TurnGain * error / rules.MaxTurnRate);

    private static sbyte Axis(ShipStats stats, in QuaternionD controlFrame, in QuaternionD axes, in Vector3D controlAxis, in Vector3D force)
    {
        var along = Vector3D.Dot(force, axes.Rotate(controlAxis));
        var available = ShipFlightModel.AvailableThrust(stats, controlFrame, along >= 0 ? controlAxis : -controlAxis);
        if (available <= 0)
        {
            // Never zero: a zero axis hands the axis to the assist, which would hover instead of letting gravity act.
            return along >= 0 ? (sbyte)1 : (sbyte)-1;
        }

        var value = (sbyte)Math.Round(Math.Clamp(along / available, -1, 1) * 127);
        return value != 0 ? value : along >= 0 ? (sbyte)1 : (sbyte)-1;
    }

    private static sbyte ToAxis(double value) => (sbyte)Math.Round(Math.Clamp(value, -1, 1) * 127);
}
