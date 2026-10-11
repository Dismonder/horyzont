using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Client.Ships;

/// <summary>
/// Tolerances of the pilot prediction (K5-25 decision 16): position max(0.05 m, 4 ulp of |p|), rotation 0.01 rad, velocity 0.1 m/s.
/// Within them a correction changes nothing.
/// </summary>
public static class ShipCorrectionPolicy
{
    /// <summary>Smallest position tolerance, metres.</summary>
    public const double PositionMeters = 0.05;

    /// <summary>Rotation tolerance, radians.</summary>
    public const double RotationRadians = 0.01;

    /// <summary>Velocity tolerance, m/s.</summary>
    public const float VelocityMetersPerSecond = 0.1f;

    /// <summary>True when the predicted state matches the server state within the tolerances.</summary>
    /// <param name="predicted">Predicted state.</param>
    /// <param name="server">Server state.</param>
    /// <returns>True within the tolerances.</returns>
    public static bool Within(in ShipMotion predicted, in ShipMotion server)
    {
        var p = server.Pose.Position;
        var magnitude = Math.Max(Math.Abs(p.X), Math.Max(Math.Abs(p.Y), Math.Abs(p.Z)));
        var ulp = 4.0 * (MathF.BitIncrement((float)magnitude) - (float)magnitude);
        if (Vector3D.Distance(predicted.Pose.Position, p) > Math.Max(PositionMeters, ulp))
        {
            return false;
        }

        var dot = Math.Abs(QuaternionD.Dot(predicted.Pose.Rotation, server.Pose.Rotation));
        var angle = 2.0 * Math.Acos(Math.Min(1.0, dot));
        return angle <= RotationRadians
            && Vector3.Distance(predicted.LinearVelocity, server.LinearVelocity) <= VelocityMetersPerSecond
            && Vector3.Distance(predicted.AngularVelocity, server.AngularVelocity) <= VelocityMetersPerSecond;
    }
}
