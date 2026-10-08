using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// State of an entity in a snapshot. Position, rotation and velocity are in the frame Frames[FrameIndex]; a field absent
/// from <paramref name="Fields"/> has its default value, and Rotation then is <see cref="Quaternion.Identity"/>.
/// The client drops a state with an older ParentSeq than known and holds a newer one until the reparent operation.
/// </summary>
/// <param name="Entity">Entity.</param>
/// <param name="Fields">Present fields.</param>
/// <param name="FrameIndex">Index into the snapshot frames.</param>
/// <param name="ParentSeq">Reparent counter of the entity.</param>
/// <param name="Position">Position.</param>
/// <param name="Rotation">Rotation.</param>
/// <param name="Velocity">Velocity.</param>
/// <param name="MotionFlags">Motion flags.</param>
public readonly record struct EntityState(
    EntityId Entity, EntityStateFields Fields, byte FrameIndex, uint ParentSeq, Vector3 Position, Quaternion Rotation, Vector3 Velocity,
    byte MotionFlags);
