using Horyzont.Core.Ids;
using Horyzont.Server.Space;

namespace Horyzont.Game.Server.Terrain;

/// <summary>
/// Holds the terrain of a planet active (K5-22 decision 4) until disposed; disposing starts the usual 60 s linger. Main thread.
/// </summary>
public sealed class PlanetTerrainLease : IDisposable
{
    private ServerPlanetTerrains? _owner;

    internal PlanetTerrainLease(ServerPlanetTerrains owner, PlanetId planet, PlanetLeaseReason reason)
    {
        _owner = owner;
        Planet = planet;
        Reason = reason;
    }

    /// <summary>Gets the planet.</summary>
    public PlanetId Planet { get; }

    /// <summary>Gets why the lease was taken.</summary>
    public PlanetLeaseReason Reason { get; }

    /// <summary>Releases the lease (once).</summary>
    public void Dispose()
    {
        var owner = _owner;
        _owner = null;
        owner?.Release(this);
    }
}
