using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>A player disconnects; stores the last kinematics (server operation).</summary>
/// <param name="Player">Player id.</param>
/// <param name="Frame">Current parent frame.</param>
/// <param name="ParentSeq">Current reparent counter.</param>
/// <param name="Position">Feet position in the parent frame.</param>
/// <param name="BodyRotation">Body rotation in the parent frame.</param>
/// <param name="Pitch">Camera pitch.</param>
public sealed record PlayerLeaveOp(
    PlayerId Player, FrameRef Frame, uint ParentSeq, Vector3 Position, Quaternion BodyRotation, float Pitch) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.PlayerLeave;
}
