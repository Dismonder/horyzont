using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;

namespace Horyzont.Core.World.Terrain;

/// <summary>Terrain voxels, implemented by the game (package 9) and by a fake in tests.</summary>
public interface ITerrainService
{
    /// <summary>True when the voxels of the area are loaded on the server.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="center">Area centre in the planet frame.</param>
    /// <param name="radius">Area radius in metres.</param>
    /// <returns>Whether the area is loaded.</returns>
    bool IsAreaLoaded(PlanetId planet, Vector3 center, float radius);

    /// <summary>Loads an area synchronously; used only when replaying the journal.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="center">Area centre in the planet frame.</param>
    /// <param name="radius">Area radius in metres.</param>
    void LoadAreaBlocking(PlanetId planet, Vector3 center, float radius);

    /// <summary>Ray against the terrain surface.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="origin">Ray origin in the planet frame.</param>
    /// <param name="direction">Normalized direction.</param>
    /// <param name="maxDistance">Maximum distance in metres.</param>
    /// <param name="hit">The hit.</param>
    /// <returns>True when the terrain was hit.</returns>
    bool Raycast(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit);

    /// <summary>
    /// Applies an edit; deterministic for the same inputs. Called after the block versions were bumped, so an implementation
    /// that stores block data can pair every block it changes with its new version.
    /// </summary>
    /// <param name="edit">The edit.</param>
    /// <param name="versions">
    /// New versions of every LOD 0 block the edit may change, sorted by key (exactly <see cref="TerrainMath.AffectedBlocks"/> with
    /// <c>TerrainRules.EditMarginVoxels</c>).
    /// </param>
    void ApplyEdit(TerrainEditOp edit, IReadOnlyList<TerrainBlockVersion> versions);
}
