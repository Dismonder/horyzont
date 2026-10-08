using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Operations;

/// <summary>Removes a block; removing the last block removes the construct.</summary>
/// <param name="Construct">Construct.</param>
/// <param name="Cell">Occupied cell.</param>
public sealed record BlockClearOp(EntityId Construct, CellPos Cell) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.BlockClear;
}
