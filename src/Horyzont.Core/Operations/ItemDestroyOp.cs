using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Operations;

/// <summary>Removes items from a slot: the only way an item leaves the world.</summary>
/// <param name="Slot">Slot.</param>
/// <param name="Type">Item type the slot must hold.</param>
/// <param name="Count">Number of items, at least 1 and at most the slot count.</param>
/// <param name="Sink">Where the items go.</param>
public sealed record ItemDestroyOp(SlotRef Slot, ItemTypeId Type, ushort Count, ItemSink Sink) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.ItemDestroy;
}
