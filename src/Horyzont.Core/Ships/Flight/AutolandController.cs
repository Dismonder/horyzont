using Horyzont.Core.Frames;

namespace Horyzont.Core.Ships.Flight;

/// <summary>
/// Control of a ship without a pilot (K5-24 decision 7, spec 02:87, 02:202): with gravity the cockpit up axis levels to the
/// local up, the assist and the gear are on and the ship descends vertically at <c>min(v_lim, max(2, h/τ))</c> without horizontal
/// motion (the up axis tilts by at most <see cref="MaxTilt"/> to cancel a drift: ships without side engines steer by tilting);
/// without gravity the ship turns its main engines against the velocity and brakes to zero. Pure.
/// </summary>
public static class AutolandController
{
    /// <summary>Smallest descent speed, m/s.</summary>
    public const double MinDescentSpeed = 2;

    /// <summary>Gravity under which the ship counts as in vacuum, m/s².</summary>
    public const double ZeroGravity = 1e-4;

    /// <summary>Largest tilt of the cockpit up axis from the local up while correcting a drift, radians.</summary>
    public const double MaxTilt = 0.35;

    /// <summary>Speed under which the vacuum brake stops turning, m/s.</summary>
    public const double RestSpeed = 0.02;

    /// <summary>Computes the control.</summary>
    /// <param name="motion">The motion.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="stats">Ship stats.</param>
    /// <param name="rules">Rules.</param>
    /// <returns>The control.</returns>
    public static ShipControl Compute(in ShipMotion motion, in FlightEnvironment environment, ShipStats stats, FlightRules rules)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(rules);
        var v = Vector3D.FromFloat(motion.LinearVelocity);
        var top = new Vector3D(0, 1, 0);
        sbyte pitch = 0, yaw = 0, roll = 0, x, y, z;
        if (environment.Gravity.Length() < ZeroGravity)
        {
            // Vacuum: point the strongest engines (cockpit up) against the velocity and brake to zero; no gear.
            if (v.Length() > RestSpeed)
            {
                ShipManeuver.TurnToward(motion, stats, rules, top, Vector3D.Normalize(-v), out pitch, out yaw, out roll);
            }

            ShipManeuver.ThrustFor(motion, stats, environment, Vector3D.Zero, out x, out y, out z);
            return new ShipControl(x, y, z, pitch, yaw, roll, ShipButtons.Assist);
        }

        var up = environment.Up;
        var limit = rules.SpeedLimit(environment.Altitude, false);
        var descent = Math.Min(limit, Math.Max(MinDescentSpeed, environment.Altitude / rules.TauSeconds));
        var wanted = up * -descent;

        // Level to the local up, tilted toward the acceleration that cancels the horizontal drift (at most MaxTilt).
        var accel = ((wanted - v) * ShipManeuver.VelocityGain) - environment.Gravity;
        var vertical = Vector3D.Dot(accel, up);
        var across = accel - (up * vertical);
        var acrossLength = across.Length();
        var direction = up;
        if (acrossLength > 1e-6 && vertical > 0)
        {
            var tilt = Math.Min(MaxTilt, Math.Atan2(acrossLength, vertical));
            direction = Vector3D.Normalize((up * Math.Cos(tilt)) + (across / acrossLength * Math.Sin(tilt)));
        }

        ShipManeuver.TurnToward(motion, stats, rules, top, direction, out pitch, out yaw, out roll);
        ShipManeuver.ThrustFor(motion, stats, environment, wanted, out x, out y, out z);
        return new ShipControl(x, y, z, pitch, 0, roll, ShipButtons.Assist | ShipButtons.LandingGear);
    }
}
