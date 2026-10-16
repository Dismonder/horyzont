using Horyzont.Core.Content;
using Horyzont.Core.Items;
using Horyzont.Core.Progress;

namespace Horyzont.Core.Crafting;

/// <summary>Which recipes a player sees (K6-32, D8); shared by the server and the client.</summary>
public static class RecipeBook
{
    /// <summary>A recipe is known when the world has its progress key (or it needs none) and the player discovered an ingredient (not fuel).</summary>
    /// <param name="recipe">The recipe.</param>
    /// <param name="items">Item keys of the world (the registry on the server, the keys of message 49 on the client).</param>
    /// <param name="discovered">Item types the player discovered.</param>
    /// <param name="worldKeys">Progress keys of the world.</param>
    /// <returns>True when the recipe is known.</returns>
    public static bool IsKnown(RecipeDef recipe, IItemTypeKeys items, ItemTypeBitSet discovered, IReadOnlySet<string> worldKeys)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (recipe.ProgressKey is not null && !worldKeys.Contains(recipe.ProgressKey))
        {
            return false;
        }

        var ingredients = recipe.Ingredients;
        for (var i = 0; i < ingredients.Count; i++)
        {
            if (items.TryGetId(ingredients[i].ItemKey, out var type) && discovered.Contains(type))
            {
                return true;
            }
        }

        return false;
    }
}
