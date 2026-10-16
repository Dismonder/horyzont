using System.Diagnostics.CodeAnalysis;

namespace Horyzont.Core.Content;

/// <summary>Survival content: items, terrain yields and the start kit. Without it the server behaves as in K2 (D8 of K6-29W).</summary>
public sealed class SurvivalContent
{
    private readonly Dictionary<string, TerrainYieldDef> _yieldByMaterial;
    private readonly Dictionary<string, RecipeDef> _recipeByKey;

    internal SurvivalContent(ItemCatalog items, TerrainYieldDef[] yields, StartKitDef startKit, RecipeDef[]? recipes = null)
    {
        Recipes = recipes ?? [];
        _recipeByKey = new Dictionary<string, RecipeDef>(Recipes.Count, StringComparer.Ordinal);
        foreach (var recipe in Recipes)
        {
            _recipeByKey.Add(recipe.Key, recipe);
        }

        Items = items;
        Yields = yields;
        StartKit = startKit;
        _yieldByMaterial = new Dictionary<string, TerrainYieldDef>(yields.Length, StringComparer.Ordinal);
        foreach (var yield in yields)
        {
            _yieldByMaterial.Add(yield.MaterialKey, yield);
        }
    }

    /// <summary>Item types.</summary>
    public ItemCatalog Items { get; }

    /// <summary>Terrain yields in file order.</summary>
    public IReadOnlyList<TerrainYieldDef> Yields { get; }

    /// <summary>Recipes in file order; empty when the content has none (K6-32: then crafting and the automatic discoveries are off).</summary>
    public IReadOnlyList<RecipeDef> Recipes { get; }

    /// <summary>Looks up a recipe by key.</summary>
    /// <param name="key">Recipe key.</param>
    /// <param name="recipe">The recipe when found.</param>
    /// <returns>True when the content has the recipe.</returns>
    public bool TryGetRecipe(string key, [NotNullWhen(true)] out RecipeDef? recipe) => _recipeByKey.TryGetValue(key, out recipe);

    /// <summary>Player containers and start kit.</summary>
    public StartKitDef StartKit { get; }

    /// <summary>Looks up the yield of a terrain material.</summary>
    /// <param name="materialKey">Material key.</param>
    /// <param name="yield">The yield when the material has one.</param>
    /// <returns>True when digging the material yields items.</returns>
    public bool TryGetYield(string materialKey, [NotNullWhen(true)] out TerrainYieldDef? yield) => _yieldByMaterial.TryGetValue(materialKey, out yield);
}
