using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Operations;

/// <summary>Sets, replaces or removes (null state) the state of a functional block (K6-30).</summary>
/// <param name="Construct">Construct.</param>
/// <param name="Cell">Cell of the block.</param>
/// <param name="ExpectedType">Block type the cell must hold.</param>
/// <param name="State">New state; null removes the state.</param>
public sealed record BlockEntitySetOp(EntityId Construct, CellPos Cell, ushort ExpectedType, BlockEntityState? State) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => (OperationKind)40; // K6-30 A2: OperationKind.BlockEntitySet
}
