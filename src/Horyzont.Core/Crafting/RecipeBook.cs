using Horyzont.Core.Content;
using Horyzont.Core.Items;
using Horyzont.Core.Progress;

namespace Horyzont.Core.Crafting;

/// <summary>Which recipes a player sees (K6-32, D8); shared by the server and the client.</summary>
public static class RecipeBook
{
    /// <summary>A recipe is known when the world has its progress key (or it needs none) and the player discovered an ingredient (not fuel).</summary>
    /// <param name="recipe">The recipe.</param>
    /// <param name="registry">Item types of the world.</param>
    /// <param name="discovered">Item types the player discovered.</param>
    /// <param name="worldKeys">Progress keys of the world.</param>
    /// <returns>True when the recipe is known.</returns>
    public static bool IsKnown(RecipeDef recipe, ItemTypeRegistry registry, ItemTypeBitSet discovered, IReadOnlySet<string> worldKeys) =>
        throw new NotImplementedException("K6-32 A6");
}
