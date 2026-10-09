using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Operations;

/// <summary>Creates items in a slot that is empty or holds the same type: the only way an item enters the world.</summary>
/// <param name="Slot">Target slot.</param>
/// <param name="Type">Item type, registered in the world.</param>
/// <param name="Count">Number of items, at least 1.</param>
/// <param name="StackLimit">Stack limit of the type (from content), which the slot may not exceed.</param>
/// <param name="Source">Where the items come from.</param>
public sealed record ItemCreateOp(SlotRef Slot, ItemTypeId Type, ushort Count, ushort StackLimit, ItemSource Source) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.ItemCreate;
}
