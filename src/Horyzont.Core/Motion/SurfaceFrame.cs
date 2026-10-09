using System.Numerics;

namespace Horyzont.Core.Motion;

/// <summary>Tangent frame of the planet surface and the meaning of <c>PlayerInput.Yaw</c>.</summary>
public static class SurfaceFrame
{
    /// <summary>Below this |projection| the +Y axis is too close to "up" and +Z is used instead (poles).</summary>
    public const float PoleFallbackThreshold = 1e-4f;

    private const float DegenerateProjection = 1e-6f;
    private const float OppositeThreshold = 1e-6f;

    /// <summary>"North" tangent at up: normalize(ProjectOnPlane(+Y, up)); near the poles normalize(ProjectOnPlane(+Z, up)).</summary>
    /// <param name="up">Unit "up" direction.</param>
    /// <returns>The unit reference tangent.</returns>
    public static Vector3 Reference(Vector3 up)
    {
        var north = ProjectOnPlane(Vector3.UnitY, up);
        var length = north.Length();
        if (length >= PoleFallbackThreshold)
        {
            return north / length;
        }

        var fallback = ProjectOnPlane(Vector3.UnitZ, up);
        length = fallback.Length();
        return length > 0f && float.IsFinite(length) ? fallback / length : Vector3.UnitZ;
    }

    /// <summary>Body rotation: basis Y = up, forward (-Z) = Reference(up) rotated by yaw about up (right-handed). Normalized.</summary>
    /// <param name="up">Unit "up" direction.</param>
    /// <param name="yaw">Yaw in radians.</param>
    /// <returns>The body rotation.</returns>
    public static Quaternion FromYaw(Vector3 up, float yaw)
    {
        var reference = Reference(up);
        var forward = (reference * MathF.Cos(yaw)) + (Vector3.Cross(up, reference) * MathF.Sin(yaw));
        var right = Vector3.Cross(forward, up);

        // Rows of the matrix are the images of the body axes X (right), Y (up) and Z (backwards).
        var basis = new Matrix4x4(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            -forward.X, -forward.Y, -forward.Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(basis));
    }

    /// <summary>Yaw of the body's forward projected on the tangent plane, in (-pi, pi]; 0 when the projection degenerates.</summary>
    /// <param name="bodyRotation">Body rotation.</param>
    /// <param name="up">Unit "up" direction.</param>
    /// <returns>The yaw.</returns>
    public static float YawOf(Quaternion bodyRotation, Vector3 up)
    {
        var forward = Vector3.Transform(-Vector3.UnitZ, bodyRotation);
        var tangent = forward - (up * Vector3.Dot(forward, up));
        var length = tangent.Length();
        if (!(length > DegenerateProjection) || !float.IsFinite(length))
        {
            return 0f;
        }

        tangent /= length;
        var reference = Reference(up);
        var yaw = MathF.Atan2(Vector3.Dot(up, Vector3.Cross(reference, tangent)), Vector3.Dot(reference, tangent));
        return yaw <= -MathF.PI ? MathF.PI : yaw;
    }

    /// <summary>Parallel transport: the minimal rotation taking the body's up to newUp, applied to the body. Normalized.</summary>
    /// <param name="bodyRotation">Body rotation.</param>
    /// <param name="newUp">New unit "up" direction.</param>
    /// <returns>The transported rotation.</returns>
    public static Quaternion TransportToUp(Quaternion bodyRotation, Vector3 newUp)
    {
        var bodyUp = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, bodyRotation));
        var axis = Vector3.Cross(bodyUp, newUp);
        var w = 1f + Vector3.Dot(bodyUp, newUp);
        Quaternion turn;
        if (w < OppositeThreshold)
        {
            // Up flips over: any axis perpendicular to the body's up is a minimal rotation, use the body's forward.
            turn = Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, bodyRotation)), MathF.PI);
        }
        else
        {
            turn = Quaternion.Normalize(new Quaternion(axis, w));
        }

        return Quaternion.Normalize(Quaternion.Concatenate(bodyRotation, turn));
    }

    /// <summary>
    /// Projection of <paramref name="axis"/> on the plane with normal <paramref name="up"/>, done twice: close to a pole the first
    /// projection is a tiny difference of large terms and keeps a component along <paramref name="up"/> that would tilt the body.
    /// </summary>
    private static Vector3 ProjectOnPlane(Vector3 axis, Vector3 up)
    {
        var projected = axis - (up * Vector3.Dot(axis, up));
        return projected - (up * Vector3.Dot(projected, up));
    }
}
