using Horyzont.Client.Inventory;
using Horyzont.Core.Content;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Client.Crafting;

/// <summary>
/// The recipes the local player sees (K6-32 D8): the recipes of a station kind with a progress key the world has (or none) and an ingredient the
/// player discovered, counted against the bag and the hotbar. The rule is the one of <c>RecipeBook.IsKnown</c>; the client has no item registry object,
/// only the keys of message 49, so the rule is applied on <see cref="ClientInventory.TryGetTypeId"/> (a test compares the two for every recipe).
/// </summary>
public sealed class RecipeList
{
    private readonly SurvivalContent _content;
    private readonly ClientInventory _inventory;
    private readonly ClientProgress _progress;

    /// <summary>Initializes a new instance of the <see cref="RecipeList"/> class.</summary>
    /// <param name="content">The game content with the recipes.</param>
    /// <param name="inventory">The inventory (item registry and counts).</param>
    /// <param name="progress">Discoveries and progress keys.</param>
    public RecipeList(SurvivalContent content, ClientInventory inventory, ClientProgress progress)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    /// <summary>True when the player knows the recipe.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <returns>Whether the recipe is visible.</returns>
    public bool IsKnown(RecipeDef recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (recipe.ProgressKey is not null && !_progress.WorldKeys.Contains(recipe.ProgressKey))
        {
            return false;
        }

        var ingredients = recipe.Ingredients;
        for (var i = 0; i < ingredients.Count; i++)
        {
            if (_inventory.TryGetTypeId(ingredients[i].ItemKey, out var type) && _progress.Discovered.Contains(type))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The recipes of a station kind the player knows, in the order of the content.</summary>
    /// <param name="station">The station kind; <see cref="StationKind.Hands"/> for the recipes of the hands.</param>
    /// <returns>The recipes with their counts.</returns>
    public IReadOnlyList<RecipeView> Visible(StationKind station)
    {
        var views = new List<RecipeView>();
        foreach (var recipe in _content.Recipes)
        {
            if (recipe.Station != station || !IsKnown(recipe))
            {
                continue;
            }

            var ingredients = Counted(recipe.Ingredients, out var haveIngredients);
            var fuel = Counted(recipe.Fuel, out var haveFuel);
            views.Add(new RecipeView(recipe, ingredients, fuel, haveIngredients && haveFuel));
        }

        return views;
    }

    private List<IngredientView> Counted(IReadOnlyList<RecipeEntry> entries, out bool enough)
    {
        enough = true;
        var views = new List<IngredientView>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var have = _inventory.TryGetTypeId(entries[i].ItemKey, out var type) ? _inventory.CountOf(type) : 0;
            enough &= have >= entries[i].Count;
            views.Add(new IngredientView(entries[i].ItemKey, entries[i].Count, have));
        }

        return views;
    }
}
