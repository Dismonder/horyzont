using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Terrain;

namespace Horyzont.Game.Client;

/// <summary>
/// Block source of a remote client until the terrain cache exists (K3-16): no block was ever edited, so Voxel Tools always falls
/// back to the generator.
/// </summary>
public sealed class GeneratorOnlyBlockSource : ITerrainBlockSource
{
    /// <inheritdoc />
    public TerrainBlockLookup TryLoad(TerrainBlockKey key, out uint version, out byte[] blob)
    {
        version = 0;
        blob = [];
        return TerrainBlockLookup.NotEdited;
    }
}
