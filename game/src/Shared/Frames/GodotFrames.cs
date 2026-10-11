using Horyzont.Core.Frames;

namespace Horyzont.Game.Frames;

/// <summary>
/// The only conversion between the double-precision frame types of Core (<see cref="Vector3D"/>, <see cref="QuaternionD"/>,
/// <see cref="PoseD"/>) and Godot (K5-20, decision 17). The engine is built with double precision, so nothing is lost. The Godot
/// world is the system frame (no floating origin). Pure, no state.
/// </summary>
public static class GodotFrames
{
    /// <summary>Converts a vector to Godot.</summary>
    /// <param name="value">The vector.</param>
    /// <returns>The Godot vector.</returns>
    public static Godot.Vector3 ToGodot(Vector3D value) => new(value.X, value.Y, value.Z);

    /// <summary>Converts a Godot vector to Core.</summary>
    /// <param name="value">The Godot vector.</param>
    /// <returns>The vector.</returns>
    public static Vector3D ToCore(Godot.Vector3 value) => new(value.X, value.Y, value.Z);

    /// <summary>Converts a rotation to Godot.</summary>
    /// <param name="value">The rotation.</param>
    /// <returns>The Godot quaternion.</returns>
    public static Godot.Quaternion ToGodot(QuaternionD value) => new(value.X, value.Y, value.Z, value.W);

    /// <summary>Converts a Godot rotation to Core.</summary>
    /// <param name="value">The Godot quaternion.</param>
    /// <returns>The rotation.</returns>
    public static QuaternionD ToCore(Godot.Quaternion value) => QuaternionD.Normalize(new QuaternionD(value.X, value.Y, value.Z, value.W));

    /// <summary>Converts a pose to a Godot transform.</summary>
    /// <param name="pose">The pose.</param>
    /// <returns>The transform.</returns>
    public static Godot.Transform3D ToGodot(in PoseD pose) => new(new Godot.Basis(ToGodot(pose.Rotation)), ToGodot(pose.Position));

    /// <summary>Converts a Godot transform (scale ignored) to a pose.</summary>
    /// <param name="transform">The transform.</param>
    /// <returns>The pose.</returns>
    public static PoseD ToCore(Godot.Transform3D transform) =>
        new(ToCore(transform.Origin), ToCore(transform.Basis.Orthonormalized().GetRotationQuaternion()));
}
