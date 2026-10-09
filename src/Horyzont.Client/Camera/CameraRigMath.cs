using System.Numerics;

using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Camera;

/// <summary>Camera origin and direction for the first- and third-person views. Pure and allocation-free.</summary>
public static class CameraRigMath
{
    /// <summary>
    /// Computes the camera pose. The eye is exactly the server formula (feet + UpAt(feet) * EyeHeight, never smoothed); only the roll
    /// of the view follows <paramref name="smoothedUp"/>. The third-person origin is before the engine's spring-arm collision.
    /// </summary>
    /// <param name="feet">Predicted feet position.</param>
    /// <param name="bodyRotation">Body rotation.</param>
    /// <param name="pitch">Pitch in radians.</param>
    /// <param name="smoothedUp">Unit up of the camera (see <see cref="CameraUpSmoother"/>).</param>
    /// <param name="mode">First or third person.</param>
    /// <param name="offsets">Third-person placement.</param>
    /// <param name="rules">Game rules (eye height).</param>
    /// <returns>The camera pose.</returns>
    public static CameraPose Compute(
        Vector3 feet, Quaternion bodyRotation, float pitch, Vector3 smoothedUp, CameraMode mode, TppOffsets offsets, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (mode is not (CameraMode.FirstPerson or CameraMode.ThirdPerson))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Only the body cameras are computed here.");
        }

        var eye = feet + (TerrainMath.UpAt(feet) * rules.EyeHeight);

        // The view heading is the body's forward carried onto the plane of the camera's up.
        var forward = Vector3.Transform(-Vector3.UnitZ, bodyRotation);
        var heading = forward - (smoothedUp * Vector3.Dot(forward, smoothedUp));
        var length = heading.Length();
        heading = length > 1e-6f ? heading / length : forward;
        var direction = Vector3.Normalize((heading * MathF.Cos(pitch)) + (smoothedUp * MathF.Sin(pitch)));
        if (mode == CameraMode.FirstPerson)
        {
            return new CameraPose(eye, direction);
        }

        var right = Vector3.Cross(direction, smoothedUp);
        var rightLength = right.Length();
        right = rightLength > 1e-6f ? right / rightLength : Vector3.Cross(direction, Vector3.UnitY);
        var cameraUp = Vector3.Cross(right, direction);
        var origin = eye - (direction * offsets.Behind) + (right * offsets.Right) + (cameraUp * offsets.Up);
        return new CameraPose(origin, direction);
    }
}
