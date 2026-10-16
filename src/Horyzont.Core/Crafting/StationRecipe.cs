namespace Horyzont.Core.Crafting;

/// <summary>A recipe of a station with time translated to item type ids (K6-32, D15); built once, read every frame by <see cref="StationProcess"/>.</summary>
/// <param name="Ingredients">Ingredients per piece.</param>
/// <param name="Fuel">Fuel per piece, consumed from the input like the ingredients.</param>
/// <param name="Result">The result per piece.</param>
public readonly record struct StationRecipe(IReadOnlyList<StationEntry> Ingredients, IReadOnlyList<StationEntry> Fuel, StationEntry Result);
