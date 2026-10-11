using Horyzont.Core.Frames;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Server.Ships;

/// <summary>A body the <see cref="DevTransitPilot"/> flies around, in the parent frame of the ship.</summary>
/// <param name="Center">Centre.</param>
/// <param name="Radius">Radius R.</param>
public readonly record struct BodySphere(Vector3D Center, double Radius);

/// <summary>
/// Development transit pilot (K5-24 decision 19; tests, <c>--dev</c> and the <c>ship pilot</c> command only — spec 02:35: no
/// autopilot for players). A pure controller with a phase: an upright lift off, a turn of the nose to the target body, a cruise on
/// the pulse drive (or on full thrust) around the bodies in the way — the hard speed limit brakes on the approach (P1) — and an
/// upright descent braking over the ground distance measured by the caller (a collider sweep), down to parking.
/// </summary>
/// <param name="usePulse">False flies on thrust only (the transit times without the pulse drive).</param>
public sealed class DevTransitPilot(bool usePulse = true)
{
    /// <summary>Height over the departure ground before the turn to the target, metres.</summary>
    public const double LiftOffMeters = 10;

    /// <summary>Climb speed of the departure and the turn, m/s.</summary>
    public const double ClimbSpeed = 25;

    /// <summary>Cosine of the nose error under which the cruise starts.</summary>
    public const double AlignedCosine = 0.995;

    /// <summary>Fraction of the net upward acceleration the braking profile relies on.</summary>
    public const double BrakeMargin = 0.85;

    /// <summary>Seconds of margin of the landing switch for the turn to upright.</summary>
    public const double TurnMarginSeconds = 1.5;

    /// <summary>Altitude kept over a body in the way, metres (its speed limit is then this altitude / τ).</summary>
    public const double AvoidAltitudeMeters = 8000;

    /// <summary>A body closer than this many radii is the one the ship is leaving, not an obstacle.</summary>
    public const double LeavingFactor = 2;

    /// <summary>Gets the phase.</summary>
    public DevTransitPhase Phase { get; private set; }

    /// <summary>Computes the control for one step.</summary>
    /// <param name="motion">The motion (parent frame).</param>
    /// <param name="stats">Ship stats.</param>
    /// <param name="environment">The environment (parent frame).</param>
    /// <param name="targetCenter">Centre of the target body in the parent frame.</param>
    /// <param name="targetRadius">Radius of the target body.</param>
    /// <param name="groundDistance">Distance from the ship down to the ground under it, metres; infinite when out of the probe.</param>
    /// <param name="rules">Rules.</param>
    /// <param name="obstacles">Other bodies in the parent frame (the target and the departure body may be included).</param>
    /// <returns>The control.</returns>
    public ShipControl Compute(
        in ShipMotion motion, ShipStats stats, in FlightEnvironment environment, in Vector3D targetCenter, double targetRadius, double groundDistance,
        FlightRules rules, ReadOnlySpan<BodySphere> obstacles = default)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(rules);
        var com = motion.Pose.Position + motion.Pose.Rotation.Rotate(Vector3D.FromFloat(stats.CenterOfMass));
        var toTarget = targetCenter - com;
        var distance = toTarget.Length();
        var nearTarget = Math.Abs(environment.RadiusOfBody - targetRadius) < 1e-3 && Math.Abs(environment.DistanceFromBody - distance) < 1;
        var aim = Aim(com, targetCenter, targetRadius, obstacles);
        var direction = aim - com;
        var aimDistance = direction.Length();
        direction = aimDistance > 1e-9 ? direction / aimDistance : environment.Up;

        if ((motion.Flags & ShipFlightFlags.Parked) != 0 && Phase == DevTransitPhase.Landing)
        {
            Phase = DevTransitPhase.Arrived;
        }

        var nose = new Vector3D(0, 0, -1);
        var top = new Vector3D(0, 1, 0);
        switch (Phase)
        {
            case DevTransitPhase.Arrived:
                return new ShipControl(0, 0, 0, 0, 0, 0, ShipButtons.Assist | ShipButtons.LandingGear);

            case DevTransitPhase.Departure:
            {
                ShipManeuver.TurnToward(motion, stats, rules, top, environment.Up, out var pitch, out _, out var roll);
                ShipManeuver.ThrustFor(motion, stats, environment, environment.Up * ClimbSpeed, out var x, out var y, out var z);
                if (groundDistance > LiftOffMeters)
                {
                    Phase = usePulse ? DevTransitPhase.Turn : DevTransitPhase.Cruise;
                }

                return new ShipControl(x, y, z, pitch, 0, roll, ShipButtons.Assist);
            }

            case DevTransitPhase.Turn:
            {
                var cosine = ShipManeuver.TurnToward(motion, stats, rules, nose, direction, out var pitch, out var yaw, out _);
                ShipManeuver.ThrustFor(motion, stats, environment, environment.Up * ClimbSpeed, out var x, out var y, out var z);
                if (cosine > AlignedCosine)
                {
                    Phase = DevTransitPhase.Cruise;
                }

                return new ShipControl(x, y, z, pitch, yaw, 0, ShipButtons.Assist);
            }

            case DevTransitPhase.Cruise:
            {
                var speed = Math.Sqrt(motion.LinearVelocity.LengthSquared());
                var atFloor = environment.Altitude / rules.TauSeconds <= rules.MinSpeedLimit;
                if (nearTarget && atFloor && groundDistance < BrakingDistance(speed, stats, environment) + (speed * TurnMarginSeconds))
                {
                    Phase = DevTransitPhase.Landing;
                    goto case DevTransitPhase.Landing;
                }

                if (usePulse)
                {
                    ShipManeuver.TurnToward(motion, stats, rules, nose, direction, out var pitch, out var yaw, out _);
                    return new ShipControl(0, 0, 0, pitch, yaw, 0, ShipButtons.Assist | ShipButtons.PulseDrive);
                }

                // Without the pulse drive the main engines push: fly upright toward the aim at the speed cap.
                ShipManeuver.TurnToward(motion, stats, rules, top, direction, out var tPitch, out _, out var tRoll);
                ShipManeuver.ThrustFor(motion, stats, environment, direction * rules.SpeedCap, out var x, out var y, out var z);
                return new ShipControl(x, y, z, tPitch, 0, tRoll, ShipButtons.Assist);
            }

            case DevTransitPhase.Landing:
            default:
            {
                ShipManeuver.TurnToward(motion, stats, rules, top, environment.Up, out var pitch, out _, out var roll);
                var limit = rules.SpeedLimit(environment.Altitude, false);
                var brake = Math.Sqrt(2 * NetBrake(stats, environment) * Math.Max(0, groundDistance - 3));
                var descent = Math.Max(AutolandController.MinDescentSpeed, Math.Min(limit, brake));
                ShipManeuver.ThrustFor(motion, stats, environment, environment.Up * -descent, out var x, out var y, out var z);
                return new ShipControl(x, y, z, pitch, 0, roll, ShipButtons.Assist | ShipButtons.LandingGear);
            }
        }
    }

    /// <summary>The aim point: the target, or a point beside the first body whose sphere of avoidance cuts the way.</summary>
    private static Vector3D Aim(in Vector3D from, in Vector3D target, double targetRadius, ReadOnlySpan<BodySphere> obstacles)
    {
        var way = target - from;
        var length = way.Length();
        if (length < 1e-9)
        {
            return target;
        }

        var unit = way / length;
        var bestT = double.PositiveInfinity;
        var aim = target;
        foreach (var body in obstacles)
        {
            if (Vector3D.Distance(body.Center, target) < targetRadius * 0.5)
            {
                continue;
            }

            var safe = body.Radius + AvoidAltitudeMeters;
            var along = Vector3D.Dot(body.Center - from, unit);
            if (along <= 0 || along >= length || Vector3D.Distance(from, body.Center) < body.Radius * LeavingFactor)
            {
                // Behind, beyond the target, or the body we are leaving (already inside its sphere).
                continue;
            }

            var closest = from + (unit * along);
            var offset = closest - body.Center;
            var miss = offset.Length();
            if (miss >= safe || along >= bestT)
            {
                continue;
            }

            var side = miss > 1e-6 ? offset / miss : Perpendicular(unit);
            bestT = along;
            aim = body.Center + (side * safe);
        }

        return aim;
    }

    private static Vector3D Perpendicular(in Vector3D unit)
    {
        var cross = Vector3D.Cross(unit, Math.Abs(unit.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0));
        return Vector3D.Normalize(cross);
    }

    private static double NetBrake(ShipStats stats, in FlightEnvironment environment)
    {
        var controlFrame = QuaternionD.Normalize(QuaternionD.FromFloat(stats.ControlFrame));
        var up = ShipFlightModel.AvailableThrust(stats, controlFrame, new Vector3D(0, 1, 0));
        var net = stats.MassKg > 0 ? (up / stats.MassKg) - environment.Gravity.Length() : 0;
        return Math.Max(0.5, BrakeMargin * net);
    }

    private static double BrakingDistance(double speed, ShipStats stats, in FlightEnvironment environment) =>
        (speed * speed) / (2 * NetBrake(stats, environment));
}
