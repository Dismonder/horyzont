using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Props;

namespace Horyzont.Core.Content;

/// <summary>Survival content: items, terrain yields, the start kit and the harvestable props. Without it the server behaves as in K2 (D8 of K6-29W).</summary>
public sealed class SurvivalContent
{
    private readonly Dictionary<string, TerrainYieldDef> _yieldByMaterial;
    private readonly PropDef?[] _propByKind;

    internal SurvivalContent(ItemCatalog items, TerrainYieldDef[] yields, StartKitDef startKit, PropDef[] props)
    {
        Items = items;
        Yields = yields;
        StartKit = startKit;
        Props = props;
        _propByKind = new PropDef?[Enum.GetValues<PropKindV1>().Max(k => (int)k) + 1];
        foreach (var prop in props)
        {
            if (PropKeys.TryGetKind(prop.Key, out var kind))
            {
                _propByKind[(int)kind] = prop;
            }
        }

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
    public IReadOnlyList<PropDef> Props { get; }

    /// <summary>Looks up the definition of a prop kind.</summary>
    /// <param name="kind">Kind of layout V1.</param>
    /// <param name="def">The definition when the content has one.</param>
    /// <returns>True when the kind is defined.</returns>
    public bool TryGetProp(PropKindV1 kind, [NotNullWhen(true)] out PropDef? def)
    {
        def = (int)kind > 0 && (int)kind < _propByKind.Length ? _propByKind[(int)kind] : null;
        return def is not null;
    }

    /// <summary>Looks up the yield of a terrain material.</summary>
    /// <param name="materialKey">Material key.</param>
    /// <param name="yield">The yield when the material has one.</param>
    /// <returns>True when digging the material yields items.</returns>
    public bool TryGetYield(string materialKey, [NotNullWhen(true)] out TerrainYieldDef? yield) => _yieldByMaterial.TryGetValue(materialKey, out yield);
}
