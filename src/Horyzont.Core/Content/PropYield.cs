namespace Horyzont.Core.Content;

/// <summary>One item of the yield of a prop (K6-31 D8).</summary>
/// <param name="ItemKey">Item key.</param>
/// <param name="Count">Number of items (1-999).</param>
public sealed record PropYield(string ItemKey, ushort Count);
