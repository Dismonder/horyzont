using Godot;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;

namespace Horyzont.Game.Frames;

/// <summary>
/// The space of one reference frame (K5-20, decision 18): where its physics runs, where the frame stands in the Godot world, its
/// gravity and whether bodies walk in it. EA: every frame shares the one physics space of the world (<see cref="SharedWorldFrameSpaces"/>).
/// </summary>
public interface IFrameSpace
{
    /// <summary>Gets the frame.</summary>
    FrameRef Frame { get; }

    /// <summary>Gets the physics space of the frame (the space of the world in EA; empty outside the scene tree).</summary>
    Rid PhysicsSpace { get; }

    /// <summary>Gets the transform of the frame in the Godot world (the system frame); identity for a frame without a node.</summary>
    Transform3D GlobalTransform { get; }

    /// <summary>Gets a value indicating whether bodies walk in the frame (planets, constructs in the static mode).</summary>
    bool SupportsWalking { get; }

    /// <summary>Gravity at a point of the frame, expressed in the frame.</summary>
    /// <param name="localPosition">The point in the frame.</param>
    /// <returns>The acceleration (m/s²).</returns>
    Vector3D GravityAt(Vector3D localPosition);
}
