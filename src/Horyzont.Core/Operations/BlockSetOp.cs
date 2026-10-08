using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Operations;

/// <summary>Places a block in an empty cell of a construct.</summary>
/// <param name="Construct">Construct.</param>
/// <param name="Cell">Empty cell.</param>
/// <param name="Block">Block.</param>
public sealed record BlockSetOp(EntityId Construct, CellPos Cell, BlockState Block) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.BlockSet;
}
