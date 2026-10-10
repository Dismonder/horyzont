namespace Horyzont.Core.Frames;

/// <summary>A double-precision pose of a frame or a body in a parent frame (K5-20).</summary>
/// <param name="Position">Origin in the parent frame (metres).</param>
/// <param name="Rotation">Unit rotation from the local axes to the parent frame.</param>
public readonly record struct PoseD(Vector3D Position, QuaternionD Rotation)
{
    /// <summary>The identity pose.</summary>
    public static PoseD Identity => new(Vector3D.Zero, QuaternionD.Identity);
}
