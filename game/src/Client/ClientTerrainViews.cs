using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Ids;
using Horyzont.Game.Terrain;

namespace Horyzont.Game.Client;

/// <summary>Hands out one <see cref="ClientTerrainView"/> per planet terrain node (the same view for the same node).</summary>
internal sealed class ClientTerrainViews
{
    private readonly Dictionary<ulong, (PlanetTerrain Node, ClientTerrainView View)> _views = [];

    internal IClientTerrainView ViewOf(PlanetId planet, PlanetTerrain node)
    {
        if (_views.TryGetValue(planet.Value, out var known) && ReferenceEquals(known.Node, node) && known.View.IsValid)
        {
            return known.View;
        }

        var view = new ClientTerrainView(node);
        _views[planet.Value] = (node, view);
        return view;
    }

    internal bool TryViewOf(PlanetId planet, PlanetTerrain? node, [NotNullWhen(true)] out IClientTerrainView? view)
    {
        view = node is null ? null : ViewOf(planet, node);
        return view is not null;
    }

    internal void Clear() => _views.Clear();
}
