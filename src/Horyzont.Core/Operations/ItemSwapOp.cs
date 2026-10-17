using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Operations;

/// <summary>Swaps the contents of two slots whose types are the expected ones.</summary>
/// <param name="A">First slot.</param>
/// <param name="B">Second slot.</param>
/// <param name="ExpectedA">Expected type in <paramref name="A"/>; <see cref="ItemTypeId.None"/> for an empty slot.</param>
/// <param name="ExpectedB">Expected type in <paramref name="B"/>; <see cref="ItemTypeId.None"/> for an empty slot.</param>
public sealed record ItemSwapOp(SlotRef A, SlotRef B, ItemTypeId ExpectedA, ItemTypeId ExpectedB) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.ItemSwap;
}
