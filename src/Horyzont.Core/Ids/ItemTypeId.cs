namespace Horyzont.Core.Ids;

/// <summary>Number of an item type in the world registry <c>item_types</c> (1..4096); 0 means "no item" (an empty slot).</summary>
/// <param name="Value">Numeric value.</param>
public readonly record struct ItemTypeId(ushort Value)
{
    /// <summary>No item (0).</summary>
    public static ItemTypeId None => default;

    /// <summary>True for <see cref="None"/>.</summary>
    public bool IsNone => Value == 0;
}
