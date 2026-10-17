using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Operations;

/// <summary>
/// Moves an entity (player or construct) to a new frame: boarding, leaving, ejection, a ship crossing spheres of
/// influence and teleports (subprojects 2 and 3). Nothing creates it in subproject 1, but the applier, codec and tests
/// exist from the start so the journal format is complete.
/// </summary>
/// <param name="Entity">Entity.</param>
/// <param name="NewParent">New parent frame.</param>
/// <param name="NewParentSeq">Current reparent counter + 1.</param>
/// <param name="LocalPosition">Position in the new frame.</param>
/// <param name="LocalRotation">Rotation in the new frame.</param>
/// <param name="LocalVelocity">Initial velocity for the game controller; not stored by Core.</param>
/// <param name="SeatCell">Seat for a player entering a construct frame.</param>
/// <param name="ResetVitals">
/// Operation 7 in format 2 (K6-35 D5): a death sets the vitals of the player to none, which the vitals service turns into the full maxima and a fresh suit
/// in the next tick. Only for a player; in a compound only as the last component (D6).
/// </param>
public sealed record EntityReparentOp(
    EntityId Entity, FrameRef NewParent, uint NewParentSeq, Vector3 LocalPosition, Quaternion LocalRotation,
    Vector3 LocalVelocity, CellPos? SeatCell, bool ResetVitals = false) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.EntityReparent;
}
