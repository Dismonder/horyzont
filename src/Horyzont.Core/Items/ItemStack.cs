using Horyzont.Core.Ids;

namespace Horyzont.Core.Items;

/// <summary>Content of a slot: a type and a count of 1..stack limit; <see cref="ItemTypeId.None"/> with count 0 is an empty slot.</summary>
/// <param name="Type">Item type.</param>
/// <param name="Count">Number of items.</param>
/// <param name="Instance">Per-instance data; always null in format 1.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Name from docs/spec/03-przetrwanie.md.")]
public readonly record struct ItemStack(ItemTypeId Type, ushort Count, ItemInstanceData? Instance = null)
{
    /// <summary>The empty slot.</summary>
    public static ItemStack Empty => default;

    /// <summary>True for an empty slot.</summary>
    public bool IsEmpty => Type.IsNone;
}
