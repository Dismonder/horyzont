using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Operations;

/// <summary>Moves items of one type from a slot to an empty slot or a slot with the same type.</summary>
/// <param name="From">Source slot.</param>
/// <param name="To">Target slot.</param>
/// <param name="Type">Item type.</param>
/// <param name="Count">Number of items, at least 1.</param>
/// <param name="ToStackLimit">Stack limit of the type (from content), which the target may not exceed.</param>
public sealed record ItemTransferOp(SlotRef From, SlotRef To, ItemTypeId Type, ushort Count, ushort ToStackLimit) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.ItemTransfer;
}
