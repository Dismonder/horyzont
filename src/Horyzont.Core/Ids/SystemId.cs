namespace Horyzont.Core.Ids;

/// <summary>Identifier of a star system; always 1 in subprojects 1–6.</summary>
/// <param name="Value">Numeric value.</param>
public readonly record struct SystemId(ulong Value);
