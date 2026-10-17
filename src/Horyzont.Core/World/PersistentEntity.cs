using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.World;

/// <summary>
/// A persistent entity that is not a construct and not a player body: a tombstone (K6-35) or an item lying in the world (K6-36). Its frame is always
/// a planet frame (D12), so taking down a building never removes it. It hosts exactly one container with the same owner.
/// </summary>
/// <param name="Id">Entity id, from the same space as constructs and players.</param>
/// <param name="Kind">Kind.</param>
/// <param name="Frame">Parent frame, a planet.</param>
/// <param name="ParentSeq">Reparent counter of the frame.</param>
/// <param name="Position">Position in the frame.</param>
/// <param name="Rotation">Rotation in the frame.</param>
/// <param name="Owner">The player who may use the container.</param>
public sealed record PersistentEntity(
    EntityId Id, PersistentEntityKind Kind, FrameRef Frame, uint ParentSeq, Vector3 Position, Quaternion Rotation, PlayerId Owner);
