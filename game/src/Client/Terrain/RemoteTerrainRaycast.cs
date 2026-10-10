using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Terrain;

namespace Horyzont.Game.Client.Terrain;

/// <summary>
/// The terrain ray of a remote client: the same ray march as the server terrain service, over the own terrain of the client. It only
/// aims; the server resolves the ray of every intent itself. Main thread; allocates no managed memory.
/// </summary>
/// <param name="terrain">The own terrain of the client.</param>
public sealed class RemoteTerrainRaycast(OwnTerrainProvider terrain) : IRemoteTerrainRaycast
{
    private readonly OwnTerrainProvider _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));

    /// <inheritdoc />
    public bool RaycastTerrain(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit)
    {
        hit = default;
        return _terrain.TryGetNode(planet, out var node) && TerrainRaycaster.Raycast(node.Tool, origin, direction, maxDistance, out hit);
    }
}
