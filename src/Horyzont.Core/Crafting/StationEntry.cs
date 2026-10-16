using Horyzont.Core.Ids;

namespace Horyzont.Core.Crafting;

/// <summary>An item type and a count of a recipe translated to the ids of a world (K6-32, D15).</summary>
/// <param name="Type">Item type.</param>
/// <param name="Count">Number of items per piece.</param>
public readonly record struct StationEntry(ItemTypeId Type, ushort Count);
