using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>A player connects (server operation).</summary>
/// <param name="Player">Player id.</param>
/// <param name="Identity">Platform identity.</param>
/// <param name="DisplayName">Display name, at most 32 UTF-8 bytes.</param>
/// <param name="Entity">Body entity.</param>
/// <param name="Frame">Parent frame.</param>
/// <param name="ParentSeq">Reparent counter.</param>
/// <param name="Position">Feet position in the parent frame.</param>
/// <param name="BodyRotation">Body rotation in the parent frame.</param>
public sealed record PlayerJoinOp(
    PlayerId Player, PlatformIdentity Identity, string DisplayName, EntityId Entity, FrameRef Frame, uint ParentSeq,
    Vector3 Position, Quaternion BodyRotation) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.PlayerJoin;
}
