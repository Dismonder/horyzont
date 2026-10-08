namespace Horyzont.Core.Ids;

/// <summary>Identifier of a planet in the world.</summary>
/// <param name="Value">Numeric value, never 0.</param>
public readonly record struct PlanetId(ulong Value);
