using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Operations;

/// <summary>
/// Switches a construct between <see cref="ConstructMode.Static"/> and <see cref="ConstructMode.Ship"/>. Carries the pose,
/// because the pose of a <see cref="ConstructMode.Ship"/> is not part of the state hash: returning to <c>Static</c> sets the
/// pose of the operation, not one from a checkpoint. Bumps the construct version by one so live observers receive the
/// operation instead of a retransfer (README Y8).
/// </summary>
/// <param name="Construct">Construct.</param>
/// <param name="Mode">New mode; differs from the current one.</param>
/// <param name="Parent">Current parent frame (a precondition: the operation never reparents).</param>
/// <param name="ParentSeq">Current reparent counter (a precondition).</param>
/// <param name="Origin">Pose position in the parent frame after the switch.</param>
/// <param name="Rotation">Pose rotation in the parent frame after the switch.</param>
public sealed record ShipModeSetOp(
    EntityId Construct, ConstructMode Mode, FrameRef Parent, uint ParentSeq, Vector3 Origin, Quaternion Rotation) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.ShipModeSet;
}
