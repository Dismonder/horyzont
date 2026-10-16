using Horyzont.Core.Content;

namespace Horyzont.Client.Crafting;

/// <summary>A recipe the player knows, with the counts the panel shows (K6-32 D8).</summary>
/// <param name="Recipe">The recipe.</param>
/// <param name="Ingredients">Ingredients with the counts carried.</param>
/// <param name="Fuel">Fuel with the counts carried (empty for instant stations).</param>
/// <param name="Affordable">True when the bag and the hotbar hold every ingredient and fuel for one piece (chests nearby are counted by the server only).</param>
public sealed record RecipeView(RecipeDef Recipe, IReadOnlyList<IngredientView> Ingredients, IReadOnlyList<IngredientView> Fuel, bool Affordable);
