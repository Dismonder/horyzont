using Horyzont.Client.Inventory;
using Horyzont.Core.Content;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Client.Crafting;

/// <summary>
/// The recipes the local player sees (K6-32 D8): the recipes of a station kind with a progress key the world has (or none) and an ingredient the
/// player discovered, counted against the bag and the hotbar. The rule is the one of <c>RecipeBook.IsKnown</c>; the client has no item registry object,
/// only the keys of message 49, so the rule is applied on <see cref="ClientInventory.TryGetTypeId"/>.
/// </summary>
public sealed class RecipeList
{
    /// <summary>Initializes a new instance of the <see cref="RecipeList"/> class.</summary>
    /// <param name="content">The game content with the recipes.</param>
    /// <param name="inventory">The inventory (item registry and counts).</param>
    /// <param name="progress">Discoveries and progress keys.</param>
    public RecipeList(SurvivalContent content, ClientInventory inventory, ClientProgress progress)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(progress);
    }

    /// <summary>True when the player knows the recipe.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <returns>Whether the recipe is visible.</returns>
    public bool IsKnown(RecipeDef recipe) => throw new NotImplementedException("K6-32 D2");

    /// <summary>The recipes of a station kind the player knows, in the order of the content.</summary>
    /// <param name="station">The station kind; <see cref="StationKind.Hands"/> for the recipes of the hands.</param>
    /// <returns>The recipes with their counts.</returns>
    public IReadOnlyList<RecipeView> Visible(StationKind station) => throw new NotImplementedException("K6-32 D2");
}
