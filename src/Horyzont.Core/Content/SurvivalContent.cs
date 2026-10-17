using System.Diagnostics.CodeAnalysis;

namespace Horyzont.Core.Content;

/// <summary>Survival content: items, terrain yields and the start kit. Without it the server behaves as in K2 (D8 of K6-29W).</summary>
public sealed class SurvivalContent
{
    private readonly Dictionary<string, TerrainYieldDef> _yieldByMaterial;

    internal SurvivalContent(ItemCatalog items, TerrainYieldDef[] yields, StartKitDef startKit)
    {
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

    /// <summary>Player containers and start kit.</summary>
    public StartKitDef StartKit { get; }

    /// <summary>Looks up the yield of a terrain material.</summary>
    /// <param name="materialKey">Material key.</param>
    /// <param name="yield">The yield when the material has one.</param>
    /// <returns>True when digging the material yields items.</returns>
    public bool TryGetYield(string materialKey, [NotNullWhen(true)] out TerrainYieldDef? yield) => _yieldByMaterial.TryGetValue(materialKey, out yield);
}
