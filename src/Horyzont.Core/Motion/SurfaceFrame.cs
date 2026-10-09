using System.Numerics;

namespace Horyzont.Core.Motion;

/// <summary>Tangent frame of the planet surface and the meaning of <c>PlayerInput.Yaw</c>.</summary>
public static class SurfaceFrame
{
    /// <summary>Below this |projection| the +Y axis is too close to "up" and +Z is used instead (poles).</summary>
    public const float PoleFallbackThreshold = 1e-4f;

    /// <summary>"North" tangent at up: normalize(ProjectOnPlane(+Y, up)); near the poles normalize(ProjectOnPlane(+Z, up)).</summary>
    /// <param name="up">Unit "up" direction.</param>
    /// <returns>The unit reference tangent.</returns>
    public static Vector3 Reference(Vector3 up) => throw new NotImplementedException();

    /// <summary>Body rotation: basis Y = up, forward (-Z) = Reference(up) rotated by yaw about up (right-handed). Normalized.</summary>
    /// <param name="up">Unit "up" direction.</param>
    /// <param name="yaw">Yaw in radians.</param>
    /// <returns>The body rotation.</returns>
    public static Quaternion FromYaw(Vector3 up, float yaw) => throw new NotImplementedException();

    /// <summary>Yaw of the body's forward projected on the tangent plane, in (-pi, pi]; 0 when the projection degenerates.</summary>
    /// <param name="bodyRotation">Body rotation.</param>
    /// <param name="up">Unit "up" direction.</param>
    /// <returns>The yaw.</returns>
    public static float YawOf(Quaternion bodyRotation, Vector3 up) => throw new NotImplementedException();

    /// <summary>Parallel transport: the minimal rotation taking the body's up to newUp, applied to the body. Normalized.</summary>
    /// <param name="bodyRotation">Body rotation.</param>
    /// <param name="newUp">New unit "up" direction.</param>
    /// <returns>The transported rotation.</returns>
    public static Quaternion TransportToUp(Quaternion bodyRotation, Vector3 newUp) => throw new NotImplementedException();
}
