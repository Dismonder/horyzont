using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Terrain;

namespace Horyzont.Game.Client;

/// <summary>Terrain of the local client of a solo or host process: the planets of the server, with no copy of its own.</summary>
public sealed class SharedTerrainProvider : IClientTerrainProvider
{
    private readonly VoxelTerrainService _service;
    private readonly ClientTerrainViews _views = new();

    /// <summary>Creates the provider over the terrain service of the server.</summary>
    /// <param name="service">The terrain service of the server.</param>
    internal SharedTerrainProvider(VoxelTerrainService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <inheritdoc />
    public void Prepare(ServerHello hello)
    {
        // The server created every planet before it listened; nothing to build for the client.
    }

    /// <inheritdoc />
    public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out IClientTerrainView? view)
    {
        _service.TryGetPlanet(planet, out var terrain);
        return _views.TryViewOf(planet, terrain, out view);
    }
}
