using Horyzont.Core.World;

namespace Horyzont.Server;

/// <summary>Creates the server terrain from the loaded world (decision D7); called once by <see cref="ServerRuntime.StartAsync"/>, after the world was loaded.</summary>
public interface IServerTerrainFactory
{
    /// <summary>Gets the terrain edit semantics version of this build (<c>TerrainEditSemantics.SemanticsVersion</c>).</summary>
    ushort SemanticsVersion { get; }

    /// <summary>Gets the Voxel Tools reference of this build (<c>voxel.ref</c> in <c>engine/engine.json</c>).</summary>
    string VoxelRef { get; }

    /// <summary>
    /// Opens <c>TerrainBlockStore(worldPath)</c>, creates <c>ServerTerrainBlocks</c> from <c>state.TerrainVersions.All</c>, the terrain
    /// service and one planet per <c>state.Planets</c> entry.
    /// </summary>
    /// <param name="worldPath">The world file.</param>
    /// <param name="state">The loaded state (the terrain versions are those of the last checkpoint).</param>
    /// <returns>The terrain; the caller shuts it down.</returns>
    IServerTerrain Create(string worldPath, WorldState state);
}
