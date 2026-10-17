using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Operations;

/// <summary>Sets the function state (bits 25-27) of a block, for example an open door or a lit lamp (K6-30).</summary>
/// <param name="Construct">Construct.</param>
/// <param name="Cell">Cell of the block.</param>
/// <param name="ExpectedType">Block type the cell must hold.</param>
/// <param name="State">New function state, 0..7.</param>
public sealed record BlockFunctionStateSetOp(EntityId Construct, CellPos Cell, ushort ExpectedType, byte State) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.BlockFunctionStateSet;
}
