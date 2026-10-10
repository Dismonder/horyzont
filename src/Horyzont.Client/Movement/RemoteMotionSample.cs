using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Client.Movement;

/// <summary>Full reconstructed motion of a remote entity in its parent frame.</summary>
/// <param name="Entity">Entity identifier.</param>
/// <param name="Frame">Parent frame.</param>
/// <param name="ParentSeq">Reparent sequence.</param>
/// <param name="ServerTick">Sample tick.</param>
/// <param name="Position">Local feet position.</param>
/// <param name="Rotation">Local unit rotation.</param>
/// <param name="Velocity">Local velocity in metres per second.</param>
/// <param name="MotionFlags">Movement mode.</param>
public readonly record struct RemoteMotionSample(EntityId Entity, FrameRef Frame, uint ParentSeq, Tick ServerTick,
    Vector3 Position, Quaternion Rotation, Vector3 Velocity, byte MotionFlags);
