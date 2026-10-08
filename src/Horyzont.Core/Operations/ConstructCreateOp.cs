using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Operations;

/// <summary>Creates a construct with one block.</summary>
/// <param name="Construct">New construct entity.</param>
/// <param name="Parent">Parent frame (a planet frame).</param>
/// <param name="Origin">Corner of cell (0,0,0) in the parent frame.</param>
/// <param name="Rotation">Rotation from construct space to the parent frame.</param>
/// <param name="FirstCell">Cell of the first block.</param>
/// <param name="Block">First block.</param>
public sealed record ConstructCreateOp(
    EntityId Construct, FrameRef Parent, Vector3 Origin, Quaternion Rotation, CellPos FirstCell, BlockState Block) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.ConstructCreate;
}
