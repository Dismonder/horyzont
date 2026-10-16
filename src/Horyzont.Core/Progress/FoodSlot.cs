using Horyzont.Core.Ids;

namespace Horyzont.Core.Progress;

/// <summary>One of the three food slots of a player (K6-33, D4): the eaten item type and the tick it was eaten at.</summary>
/// <param name="Type">The item type, <see cref="ItemTypeId.None"/> for an empty slot.</param>
/// <param name="Eaten">The tick the food was eaten at; <c>Tick(0)</c> for an empty slot.</param>
public readonly record struct FoodSlot(ItemTypeId Type, Tick Eaten)
{
    /// <summary>Gets a value indicating whether the slot is empty (type 0).</summary>
    public bool IsEmpty => Type.Value == 0;
}
