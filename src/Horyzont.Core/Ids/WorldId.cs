namespace Horyzont.Core.Ids;

/// <summary>Identifier of a saved world.</summary>
/// <param name="Value">Unique value chosen when the world is created.</param>
public readonly record struct WorldId(Guid Value);
