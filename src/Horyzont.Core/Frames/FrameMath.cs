using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.Frames;

/// <summary>
/// Pure double-precision composition of reference frames (K5-20). <see cref="TryToFloat"/> is the only place where a pose is
/// narrowed from double to the float values of the journal, the save and the network.
/// </summary>
public static class FrameMath
{
    /// <summary>Largest deviation of a rotation length from 1 that <see cref="TryToFloat"/> accepts (and normalizes away).</summary>
    public const double RotationTolerance = 1e-6;

    /// <summary>Pose of <paramref name="local"/> (given in the frame <paramref name="parent"/> describes) in the parent's parent.</summary>
    /// <param name="parent">Pose of a frame in its parent.</param>
    /// <param name="local">Pose in that frame.</param>
    /// <returns>The composed pose; its rotation is normalized.</returns>
    public static PoseD Compose(in PoseD parent, in PoseD local) =>
        new(parent.Position + parent.Rotation.Rotate(local.Position), QuaternionD.Normalize(parent.Rotation * local.Rotation));

    /// <summary>Inverse pose: the parent frame expressed in the local frame.</summary>
    /// <param name="pose">The pose.</param>
    /// <returns>The inverse.</returns>
    public static PoseD Inverse(in PoseD pose)
    {
        var inverse = QuaternionD.Normalize(QuaternionD.Conjugate(pose.Rotation));
        return new PoseD(-inverse.Rotate(pose.Position), inverse);
    }

    /// <summary>Pose <paramref name="to"/> expressed in pose <paramref name="from"/> (both in one common frame).</summary>
    /// <param name="from">Reference pose.</param>
    /// <param name="to">Pose to express.</param>
    /// <returns><c>Inverse(from) ∘ to</c>.</returns>
    public static PoseD Relative(in PoseD from, in PoseD to)
    {
        var inverse = QuaternionD.Normalize(QuaternionD.Conjugate(from.Rotation));
        return new PoseD(inverse.Rotate(to.Position - from.Position), QuaternionD.Normalize(inverse * to.Rotation));
    }

    /// <summary>Point of the local frame expressed in the parent frame.</summary>
    /// <param name="pose">Pose of the local frame.</param>
    /// <param name="point">Local point.</param>
    /// <returns>The point in the parent frame.</returns>
    public static Vector3D TransformPoint(in PoseD pose, Vector3D point) => pose.Position + pose.Rotation.Rotate(point);

    /// <summary>Direction (or velocity) of the local frame expressed in the parent frame.</summary>
    /// <param name="pose">Pose of the local frame.</param>
    /// <param name="direction">Local direction.</param>
    /// <returns>The direction in the parent frame.</returns>
    public static Vector3D TransformDirection(in PoseD pose, Vector3D direction) => pose.Rotation.Rotate(direction);

    /// <summary>Point of the parent frame expressed in the local frame.</summary>
    /// <param name="pose">Pose of the local frame.</param>
    /// <param name="point">Point in the parent frame.</param>
    /// <returns>The local point.</returns>
    public static Vector3D InverseTransformPoint(in PoseD pose, Vector3D point) =>
        QuaternionD.Conjugate(pose.Rotation).Rotate(point - pose.Position);

    /// <summary>Direction of the parent frame expressed in the local frame.</summary>
    /// <param name="pose">Pose of the local frame.</param>
    /// <param name="direction">Direction in the parent frame.</param>
    /// <returns>The local direction.</returns>
    public static Vector3D InverseTransformDirection(in PoseD pose, Vector3D direction) => QuaternionD.Conjugate(pose.Rotation).Rotate(direction);

    /// <summary>Widens a float pose: the position exactly, the rotation normalized in double (float rotations are unit only to ~1e-7).</summary>
    /// <param name="position">Position.</param>
    /// <param name="rotation">Rotation.</param>
    /// <returns>The pose.</returns>
    public static PoseD FromFloat(Vector3 position, Quaternion rotation) =>
        new(Vector3D.FromFloat(position), QuaternionD.Normalize(QuaternionD.FromFloat(rotation)));

    /// <summary>
    /// Narrows a pose to the float values a frame of <paramref name="kind"/> stores: every value finite, every coordinate within
    /// <see cref="FrameLimits.MaxCoordinate"/>, rotation length within <see cref="RotationTolerance"/> of 1 (normalized before narrowing).
    /// </summary>
    /// <param name="pose">The pose.</param>
    /// <param name="kind">Kind of the frame the pose is in.</param>
    /// <param name="position">Receives the position.</param>
    /// <param name="rotation">Receives the normalized rotation.</param>
    /// <returns>False when the pose cannot be stored in that frame.</returns>
    public static bool TryToFloat(in PoseD pose, FrameKind kind, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.Identity;
        var limit = FrameLimits.MaxCoordinate(kind);
        var p = pose.Position;
        var q = pose.Rotation;
        if (!p.IsFinite || !q.IsFinite || Math.Abs(p.X) > limit || Math.Abs(p.Y) > limit || Math.Abs(p.Z) > limit
            || Math.Abs(q.Length() - 1) > RotationTolerance)
        {
            return false;
        }

        var unit = QuaternionD.Normalize(q);
        position = new Vector3((float)p.X, (float)p.Y, (float)p.Z);
        rotation = new Quaternion((float)unit.X, (float)unit.Y, (float)unit.Z, (float)unit.W);
        return true;
    }

    /// <summary>Narrows a velocity or direction to float with a finiteness check (no range limit).</summary>
    /// <param name="vector">The vector.</param>
    /// <param name="value">Receives the float vector.</param>
    /// <returns>False when a component is not finite in double or float.</returns>
    public static bool TryVectorToFloat(Vector3D vector, out Vector3 value)
    {
        value = new Vector3((float)vector.X, (float)vector.Y, (float)vector.Z);
        return vector.IsFinite && float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}
