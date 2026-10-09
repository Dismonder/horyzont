namespace Horyzont.Core.World.Blocks;

/// <summary>Shape of a block type. Other shapes are prefab meshes from game content.</summary>
public enum BlockShape : byte
{
    /// <summary>Full 1 m cube.</summary>
    Cube = 1,

    /// <summary>Prefab triangle mesh from game content (slab, slope); the mesh is not part of the world state.</summary>
    Mesh = 2,
}
