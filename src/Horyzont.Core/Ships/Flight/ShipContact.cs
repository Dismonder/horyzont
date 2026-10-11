using System.Numerics;

using Horyzont.Core.Frames;

namespace Horyzont.Core.Ships.Flight;

/// <summary>
/// Response to a collision of a sweep (K5-24 decision 9, P8): the ship stops at the safe fraction, the velocity into the normal is
/// removed (inelastic), the tangential velocity is multiplied by the friction; ground under the gear at a low speed parks the ship.
/// Blocks are never destroyed. Shared by the server flight and the test colliders.
/// </summary>
public static class ShipContact
{
    /// <summary>Applies a sweep result to the motion whose pose is the proposed end of the sweep.</summary>
    /// <param name="motion">Motion after <see cref="ShipFlightModel.Step"/>.</param>
    /// <param name="from">Pose at the start of the step.</param>
    /// <param name="hit">The sweep result.</param>
    /// <param name="environment">The environment of the step (up direction).</param>
    /// <param name="rules">Rules.</param>
    /// <returns>True when the ship parked.</returns>
    public static bool Resolve(ref ShipMotion motion, in PoseD from, in ShipSweepResult hit, in FlightEnvironment environment, FlightRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!hit.Hit)
        {
            return false;
        }

        var fraction = Math.Clamp(hit.Fraction, 0, 1);
        var to = motion.Pose;
        motion.Pose = new PoseD(from.Position + ((to.Position - from.Position) * fraction), from.Rotation);
        var v = Vector3D.FromFloat(motion.LinearVelocity);
        var n = hit.Normal;
        var into = Vector3D.Dot(v, n);
        if (into < 0)
        {
            v -= n * into;
        }

        v *= rules.ContactFriction;
        motion.AngularVelocity = Vector3.Zero;
        if (Vector3D.Dot(n, environment.Up) >= rules.GearContactMaxTilt
            && (motion.Flags & ShipFlightFlags.GearDown) != 0
            && v.Length() < rules.ParkSpeed)
        {
            motion.LinearVelocity = Vector3.Zero;
            motion.Flags |= ShipFlightFlags.Parked;
            return true;
        }

        motion.LinearVelocity = new Vector3((float)v.X, (float)v.Y, (float)v.Z);
        return false;
    }
}
