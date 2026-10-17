using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World;

namespace Horyzont.Core.Operations;

/// <summary>
/// Creates a persistent entity (K6-35, operation 38): a tombstone or an item lying in the world, in a planet frame. The container it hosts is created by the
/// next component of the same compound. The pose is final: the server prepares it before the operation (D16).
/// </summary>
/// <param name="Entity">The new id, unused in the whole entity space.</param>
/// <param name="EntityKind">Kind of the entity.</param>
/// <param name="Frame">The planet frame.</param>
/// <param name="ParentSeq">Reparent counter of the frame.</param>
/// <param name="Position">Position in the frame.</param>
/// <param name="Rotation">Rotation in the frame.</param>
/// <param name="Owner">The player who owns the entity and its container.</param>
public sealed record PersistentEntityCreateOp(
    EntityId Entity, PersistentEntityKind EntityKind, FrameRef Frame, uint ParentSeq, Vector3 Position, Quaternion Rotation, PlayerId Owner) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.PersistentEntityCreate;
}
