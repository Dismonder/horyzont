namespace Horyzont.Core.Content;

/// <summary>An item of a recipe: an item key and a count (K6-32, D4).</summary>
/// <param name="ItemKey">Item key, e.g. <c>core:ore</c>.</param>
/// <param name="Count">Number of items.</param>
public readonly record struct RecipeEntry(string ItemKey, ushort Count);
