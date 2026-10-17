using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Client.Inventory;

/// <summary>Items that the player took in hand: they stay in the source slot until the server moves them (decision D15).</summary>
/// <param name="Source">The slot they were taken from.</param>
/// <param name="Type">Item type.</param>
/// <param name="Count">Number of items in hand.</param>
/// <param name="SourceCount">Number of items the source slot held when they were taken.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Name from the plan K6-29W.")]
public readonly record struct HeldStack(SlotRef Source, ItemTypeId Type, ushort Count, ushort SourceCount);
