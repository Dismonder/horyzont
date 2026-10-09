using Godot;

using Horyzont.Constructs.Meshing;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Game.Constructs;

/// <summary>
/// Builds the mesh of a single block (for example the placement ghost of K2-13) with the same mesher and materials as chunks.
/// Coordinates are relative to the corner of the cell: the block occupies [0,1]³. Main thread only; the caller caches the result.
/// </summary>
/// <param name="table">Render table of the world session.</param>
public sealed class BlockMeshFactory(BlockRenderTable table)
{
    private readonly BlockRenderTable _table = table ?? throw new ArgumentNullException(nameof(table));
    private readonly ChunkMeshInput _input = new();
    private readonly ChunkMesher _mesher = new();

    /// <summary>Creates the mesh of one block, rendered like in a chunk (a lone cube, so all six faces).</summary>
    /// <param name="typeId">Block type id of the world registry.</param>
    /// <param name="orientation">Orientation 0-23.</param>
    /// <returns>The mesh, or null when the id is empty.</returns>
    public ArrayMesh? CreateBlockMesh(ushort typeId, byte orientation)
    {
        if (typeId == 0)
        {
            return null;
        }

        _input.Reset(new Horyzont.Core.Checkpoints.ConstructChunkKey(default, default), 0);
        _input.Cells[ChunkMeshInput.Index(0, 0, 0)] = BlockState.Create(typeId, orientation).Packed;
        using var output = new ChunkMeshData();
        _mesher.Build(_input, _table, output);
        return ChunkMeshUploader.CreateMesh(output);
    }
}
