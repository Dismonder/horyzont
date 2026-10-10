using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Tools;

/// <summary>Neutral raycast until remote terrain replication is available.</summary>
public sealed class NoRemoteTerrain : IRemoteTerrainRaycast
{
    /// <summary>Gets the stateless default instance.</summary>
    public static NoRemoteTerrain Instance { get; } = new();

    /// <inheritdoc />
    public bool RaycastTerrain(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit)
    {
        hit = default;
        return false;
    }
}
