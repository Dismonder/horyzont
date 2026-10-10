using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Tools;

/// <summary>Read-only raycasting against a remote client's terrain.</summary>
public interface IRemoteTerrainRaycast
{
    /// <summary>Finds the nearest terrain hit in the planet frame.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="origin">Ray origin.</param>
    /// <param name="direction">Unit direction.</param>
    /// <param name="maxDistance">Maximum distance.</param>
    /// <param name="hit">Hit when found.</param>
    /// <returns>True on a hit.</returns>
    bool RaycastTerrain(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit);
}
