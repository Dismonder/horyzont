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

    /// <summary>Whether a point lies inside solid terrain (SDF below zero); used to detect ship blocks embedded in the ground.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="position">Point in the planet frame.</param>
    /// <param name="solid">True when the point is inside the terrain; false otherwise or when the answer is unknown.</param>
    /// <returns>False when the area is not loaded (no answer: never read as "not embedded"); true when <paramref name="solid"/> is valid.</returns>
    bool TrySampleSolid(PlanetId planet, Vector3 position, out bool solid);

    /// <summary>
    /// Counts the LOD 0 voxels whose centre lies inside a sphere: solid ones (SDF &lt; 0) by world material id and empty ones. Called only
    /// by intent resolution on the main thread, never by the applier or replay (its result goes into the operation), so it need not be
    /// deterministic across runs (K6-29W D9). Must not allocate.
    /// </summary>
    /// <param name="planet">Planet.</param>
    /// <param name="center">Sphere centre in the planet frame.</param>
    /// <param name="radius">Sphere radius in metres.</param>
    /// <param name="solidVoxelsByMaterial">256 counters indexed by material id; the method clears them first (index 0 stays unused).</param>
    /// <param name="airVoxels">Number of empty voxels.</param>
    /// <returns>False when the voxels cannot be read (area not loaded, no terrain); the counters are then zero.</returns>
    bool MeasureSphere(PlanetId planet, Vector3 center, float radius, Span<int> solidVoxelsByMaterial, out int airVoxels);

    /// <summary>
    /// The radius of the surface of the generator in a direction, without the edits of players (K6-31 D6): where the props stand. Called by
    /// the prop field (validator, resolvers, regrowth) on the main thread only; its result never enters an operation but the anchors derived
    /// from it decide which props exist for the server, so the client must sample the same way.
    /// </summary>
    /// <param name="planet">Planet.</param>
    /// <param name="direction">Unit direction from the planet centre.</param>
    /// <param name="radius">Distance of the generated surface from the centre, metres.</param>
    /// <returns>False for an unknown planet or a planet without a generator; <paramref name="radius"/> is then 0.</returns>
    bool TrySampleGeneratedSurface(PlanetId planet, Vector3 direction, out float radius);

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
