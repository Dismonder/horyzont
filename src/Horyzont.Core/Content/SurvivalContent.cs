using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Props;

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

    /// <summary>Harvestable props in file order; empty when the content has no <c>props/props.json</c> (K6-31 D21).</summary>
    public IReadOnlyList<PropDef> Props { get; } = []; // K6-31 B3.

    /// <summary>Looks up the definition of a prop kind.</summary>
    /// <param name="kind">Kind of layout V1.</param>
    /// <param name="def">The definition when the content has one.</param>
    /// <returns>True when the kind is defined.</returns>
#pragma warning disable CA1822 // K6-31 B3: the skeleton does not read the instance yet.
    public bool TryGetProp(PropKindV1 kind, [NotNullWhen(true)] out PropDef? def)
    {
        def = null; // K6-31 B3.
        return false;
    }
#pragma warning restore CA1822

    /// <summary>Looks up the yield of a terrain material.</summary>
    /// <param name="materialKey">Material key.</param>
    /// <param name="yield">The yield when the material has one.</param>
    /// <returns>True when digging the material yields items.</returns>
    public bool TryGetYield(string materialKey, [NotNullWhen(true)] out TerrainYieldDef? yield) => _yieldByMaterial.TryGetValue(materialKey, out yield);
}
