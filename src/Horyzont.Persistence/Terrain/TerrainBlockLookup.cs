namespace Horyzont.Persistence.Terrain;

/// <summary>Result of <see cref="ITerrainBlockSource.TryLoad(Horyzont.Core.World.Terrain.TerrainBlockKey, out uint, out byte[])"/>.</summary>
public enum TerrainBlockLookup
{
    /// <summary>The block was never edited: the voxel stream lets the generator produce it.</summary>
    NotEdited = 0,

    /// <summary>The block has a version and its data was found.</summary>
    Found = 1,

    /// <summary>
    /// The block cannot be served: the source is closed, the read failed, or the block has a version but no saved data
    /// (a broken invariant). The stream reports an error and never falls back to the generator.
    /// </summary>
    Unavailable = 2,
}
