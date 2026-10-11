using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Ships.Flight;

/// <summary>
/// The flight model (K5-24 decision 6): pure, deterministic, allocation free, one fixed order of actions — parking, rotation,
/// thrust and assist, pulse drive, acceleration, limiter and gates, position, fuel. Shared with the pilot prediction (K5-25):
/// a change of the order or of a formula is a contract change. No trigonometry: the rotation increment is a normalized
/// first-order quaternion. The model knows no collisions; <see cref="ShipContact"/> resolves them.
/// </summary>
public static class ShipFlightModel
{
    private const double MegaNewton = 1e-6;

    /// <summary>Advances the motion by one step; the new pose is the proposed pose (before collisions).</summary>
    /// <param name="motion">The motion, advanced in place.</param>
    /// <param name="control">Pilot control (or the autoland control).</param>
    /// <param name="stats">Ship stats.</param>
    /// <param name="environment">Surroundings at the start of the step.</param>
    /// <param name="rules">Rules.</param>
    /// <param name="dt">Step length, seconds (positive).</param>
    /// <returns>The outcome.</returns>
    public static FlightStepOutcome Step(
        ref ShipMotion motion, in ShipControl control, ShipStats stats, in FlightEnvironment environment, FlightRules rules, float dt)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(rules);
        double h = dt;
        var buttons = control.Buttons;
        var flags = motion.Flags & (ShipFlightFlags.Parked | ShipFlightFlags.Autoland | ShipFlightFlags.TerrainHold | ShipFlightFlags.TerrainUnavailable);
        var assist = (buttons & ShipButtons.Assist) != 0;
        if (assist)
        {
            flags |= ShipFlightFlags.Assist;
        }

        if ((buttons & ShipButtons.LandingGear) != 0)
        {
            flags |= ShipFlightFlags.GearDown;
        }

        var reserve = IsReserve(motion.Fuel, stats, rules);
        if (reserve)
        {
            flags |= ShipFlightFlags.Reserve;
        }

        // 1. Parking: a parked ship leaves only with the gear up and no launch block.
        if ((flags & ShipFlightFlags.Parked) != 0)
        {
            motion.LinearVelocity = Vector3.Zero;
            motion.AngularVelocity = Vector3.Zero;
            if ((buttons & ShipButtons.LandingGear) != 0 || environment.LaunchBlock != ShipLaunchBlock.None)
            {
                motion.Flags = flags | ShipFlightFlags.GearDown;
                return new FlightStepOutcome(rules.SpeedLimit(environment.Altitude, false), 0, 0);
            }

            flags &= ~ShipFlightFlags.Parked;
        }

        var hasFuel = motion.Fuel > 0f;
        var controlFrame = QuaternionD.Normalize(QuaternionD.FromFloat(stats.ControlFrame));

        // 2. Rotation in the control axes, limited by the torque through the inertia of each axis; turns about the centre of mass.
        var omega = QuaternionD.Conjugate(controlFrame).Rotate(Vector3D.FromFloat(motion.AngularVelocity));
        omega = new Vector3D(
            Turn(omega.X, control.Pitch, controlFrame.Rotate(new Vector3D(1, 0, 0)), stats, rules, assist, h),
            Turn(omega.Y, control.Yaw, controlFrame.Rotate(new Vector3D(0, 1, 0)), stats, rules, assist, h),
            Turn(omega.Z, control.Roll, controlFrame.Rotate(new Vector3D(0, 0, 1)), stats, rules, assist, h));
        var omegaConstruct = controlFrame.Rotate(omega);
        var rotation = motion.Pose.Rotation;
        var com = Vector3D.FromFloat(stats.CenterOfMass);
        var comPosition = motion.Pose.Position + rotation.Rotate(com);
        var increment = QuaternionD.Normalize(new QuaternionD(omegaConstruct.X * h / 2, omegaConstruct.Y * h / 2, omegaConstruct.Z * h / 2, 1));
        var newRotation = QuaternionD.Normalize(rotation * increment);
        var origin = comPosition - newRotation.Rotate(com);

        // 3. Thrust per control axis; the assist damps the velocity and compensates gravity on axes without input.
        var axes = newRotation * controlFrame;
        var axisX = axes.Rotate(new Vector3D(1, 0, 0));
        var axisY = axes.Rotate(new Vector3D(0, 1, 0));
        var axisZ = axes.Rotate(new Vector3D(0, 0, 1));
        var v = Vector3D.FromFloat(motion.LinearVelocity);
        var g = environment.Gravity;
        double mass = stats.MassKg;
        var pulse = hasFuel && !reserve && (buttons & ShipButtons.PulseDrive) != 0 && stats.PulseDrives > 0;
        double fx = 0, fy = 0, fz = 0;
        if (hasFuel && mass > 0)
        {
            fx = AxisForce(control.ThrustX, axisX, new Vector3D(1, 0, 0), controlFrame, stats, v, g, mass, rules, assist);
            fy = AxisForce(control.ThrustY, axisY, new Vector3D(0, 1, 0), controlFrame, stats, v, g, mass, rules, assist);
            fz = AxisForce(control.ThrustZ, axisZ, new Vector3D(0, 0, 1), controlFrame, stats, v, g, mass, rules, assist && !pulse);
        }

        var force = (axisX * fx) + (axisY * fy) + (axisZ * fz);
        var thrust = Math.Abs(fx) + Math.Abs(fy) + Math.Abs(fz);

        // 5. Acceleration (semi-implicit Euler: the velocity first, the position after the limiter).
        var acceleration = g - (v * environment.DragPerSecond);
        if (mass > 0)
        {
            acceleration += force / mass;
        }

        v += acceleration * h;

        // 4. Pulse drive: the forward speed follows the pulse limit with the response time ("holds the limit").
        if (pulse)
        {
            flags |= ShipFlightFlags.PulseEngaged;
            var forward = -axisZ;
            var limit = rules.SpeedLimit(environment.Altitude, true);
            var along = Vector3D.Dot(v, forward);
            if (along < limit)
            {
                v += forward * ((limit - along) * Math.Min(1, h / rules.PulseResponseSeconds));
            }
        }

        // 6. Limiter and gates.
        var speedLimit = rules.SpeedLimit(environment.Altitude, pulse);
        var speed = v.Length();
        if (speed > speedLimit)
        {
            v *= speedLimit / speed;
        }

        var up = environment.Up;
        if (double.IsFinite(environment.HorizontalSpeedCap))
        {
            var vertical = up * Vector3D.Dot(v, up);
            var across = v - vertical;
            var acrossSpeed = across.Length();
            if (acrossSpeed > environment.HorizontalSpeedCap)
            {
                v = vertical + (across * (environment.HorizontalSpeedCap / acrossSpeed));
            }
        }

        var radial = Vector3D.Dot(v, up);
        if (radial < 0 && BlocksDescent(environment, rules))
        {
            v -= up * radial;
        }
        else if (radial > 0 && reserve)
        {
            v -= up * radial;
        }

        if (environment.InSystemFrame)
        {
            var distance = origin.Length();
            if (distance > rules.SystemBoundaryMeters)
            {
                var outward = origin / distance;
                var away = Vector3D.Dot(v, outward);
                if (away > 0)
                {
                    v -= outward * away;
                }
            }
        }

        origin += v * h;

        // 7. Fuel.
        var burn = (thrust * rules.FuelPerMegaNewtonSecond * MegaNewton * h) + (pulse ? rules.PulseFuelPerSecond * h : 0);
        var fuel = Math.Max(0, motion.Fuel - burn);
        motion.Fuel = (float)fuel;
        if (IsReserve(motion.Fuel, stats, rules))
        {
            flags |= ShipFlightFlags.Reserve;
        }
        else
        {
            flags &= ~ShipFlightFlags.Reserve;
        }

        motion.Pose = new PoseD(origin, newRotation);
        motion.LinearVelocity = ToFloat(v);
        motion.AngularVelocity = ToFloat(omegaConstruct);
        motion.Flags = flags;
        return new FlightStepOutcome(speedLimit, thrust, burn);
    }

    /// <summary>True when the fuel is in the reserve of the tank capacity.</summary>
    /// <param name="fuel">Fuel units.</param>
    /// <param name="stats">Ship stats.</param>
    /// <param name="rules">Rules.</param>
    /// <returns>True in the reserve.</returns>
    public static bool IsReserve(float fuel, ShipStats stats, FlightRules rules)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(rules);
        return fuel <= rules.ReserveFraction * stats.FuelCapacity;
    }

    /// <summary>True when the terrain gate stops the motion toward the body centre.</summary>
    /// <param name="environment">The environment.</param>
    /// <param name="rules">Rules.</param>
    /// <returns>True when the descent is blocked.</returns>
    public static bool BlocksDescent(in FlightEnvironment environment, FlightRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return environment.Gate switch
        {
            ShipTerrainGate.NotLoaded => environment.DistanceFromBody < rules.NotLoadedFloorFactor * environment.RadiusOfBody,
            ShipTerrainGate.CollisionPending => environment.Altitude < rules.CollisionPendingAltitude,
            ShipTerrainGate.Unavailable => true,
            _ => false,
        };
    }

    /// <summary>Thrust available toward a control direction: the engines pushing toward the matching construct face.</summary>
    /// <param name="stats">Ship stats.</param>
    /// <param name="controlFrame">Rotation from the control axes to the construct axes.</param>
    /// <param name="controlDirection">Unit control axis direction.</param>
    /// <returns>The thrust, N.</returns>
    public static double AvailableThrust(ShipStats stats, in QuaternionD controlFrame, in Vector3D controlDirection)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return stats.Thrust[FaceOf(controlFrame.Rotate(controlDirection))];
    }

    private static double AxisForce(
        sbyte input, in Vector3D axis, in Vector3D controlAxis, in QuaternionD controlFrame, ShipStats stats, in Vector3D v, in Vector3D g,
        double mass, FlightRules rules, bool assist)
    {
        var positive = AvailableThrust(stats, controlFrame, controlAxis);
        var negative = AvailableThrust(stats, controlFrame, -controlAxis);
        if (input != 0)
        {
            double value = ShipControl.Axis(input);
            return value > 0 ? value * positive : value * negative;
        }

        if (!assist)
        {
            return 0;
        }

        var wanted = mass * ((-rules.AssistLinearGain * Vector3D.Dot(v, axis)) - Vector3D.Dot(g, axis));
        return Math.Clamp(wanted, -negative, positive);
    }

    private static double Turn(double current, sbyte input, in Vector3D constructAxis, ShipStats stats, FlightRules rules, bool assist, double h)
    {
        double target;
        if (input != 0)
        {
            target = ShipControl.Axis(input) * rules.MaxTurnRate;
        }
        else if (assist)
        {
            target = 0;
        }
        else
        {
            return current;
        }

        var inertia = Inertia(stats.Inertia, constructAxis);
        var step = inertia > 0 ? stats.TorqueNewtonMeters / inertia * h : 0;
        return current + Math.Clamp(target - current, -step, step);
    }

    private static double Inertia(in ShipInertia i, in Vector3D a) =>
        (a.X * ((i.Xx * a.X) + (i.Xy * a.Y) + (i.Xz * a.Z)))
        + (a.Y * ((i.Xy * a.X) + (i.Yy * a.Y) + (i.Yz * a.Z)))
        + (a.Z * ((i.Xz * a.X) + (i.Yz * a.Y) + (i.Zz * a.Z)));

    private static Face FaceOf(in Vector3D d)
    {
        double ax = Math.Abs(d.X), ay = Math.Abs(d.Y), az = Math.Abs(d.Z);
        if (ax >= ay && ax >= az)
        {
            return d.X >= 0 ? Face.PosX : Face.NegX;
        }

        if (ay >= az)
        {
            return d.Y >= 0 ? Face.PosY : Face.NegY;
        }

        return d.Z >= 0 ? Face.PosZ : Face.NegZ;
    }

    private static Vector3 ToFloat(in Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
