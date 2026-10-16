using Horyzont.Core.World.Blocks;

namespace Horyzont.Core.Content;

/// <summary>A recipe from <c>content/recipes/recipes.json</c> (K6-32, D4); parsing and validation arrive with <see cref="ContentLoader"/> in step B3.</summary>
/// <param name="Key">Unique key, 1 to 64 bytes of UTF-8.</param>
/// <param name="Station">The station kind that makes it; <see cref="StationKind.Hands"/> for the recipes of the hands.</param>
/// <param name="Level">Station level the recipe needs.</param>
/// <param name="Ingredients">Ingredients, 1 to 8.</param>
/// <param name="Fuel">Fuel, 0 to 2; only for stations with time.</param>
/// <param name="Result">The result.</param>
/// <param name="TimeSeconds">Seconds per piece for stations with time, 0 for the others.</param>
/// <param name="ProgressKey">Progress key the world needs for the recipe, null for none.</param>
public sealed record RecipeDef(
    string Key,
    StationKind Station,
    byte Level,
    IReadOnlyList<RecipeEntry> Ingredients,
    IReadOnlyList<RecipeEntry> Fuel,
    RecipeEntry Result,
    uint TimeSeconds,
    string? ProgressKey);
