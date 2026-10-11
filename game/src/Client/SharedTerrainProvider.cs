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
    private readonly Func<PlanetId, IDisposable?>? _acquire;
    private readonly SortedDictionary<ulong, IDisposable> _leases = [];

    /// <summary>Creates the provider over the terrain service of the server.</summary>
    /// <param name="service">The terrain service of the server.</param>
    /// <param name="acquire">Takes a lease on a server planet terrain (K5-22: <c>ServerPlanetTerrains.Acquire</c>); null = no leases.</param>
    internal SharedTerrainProvider(VoxelTerrainService service, Func<PlanetId, IDisposable?>? acquire = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _acquire = acquire;
    }

    /// <inheritdoc />
    /// <remarks>The host keeps one terrain instance: the node lives while the server <b>or</b> the client wants it (K5-22 decision 10).</remarks>
    public bool Activate(PlanetId planet)
    {
        if (_leases.ContainsKey(planet.Value))
        {
            return true;
        }

        if (_acquire?.Invoke(planet) is not { } lease)
        {
            return _service.TryGetPlanet(planet, out _);
        }

        _leases.Add(planet.Value, lease);
        return true;
    }

    /// <inheritdoc />
    public void Release(PlanetId planet)
    {
        if (_leases.Remove(planet.Value, out var lease))
        {
            lease.Dispose();
        }
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
