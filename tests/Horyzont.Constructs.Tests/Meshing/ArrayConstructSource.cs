using Horyzont.Constructs.Sources;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Constructs.Tests.Meshing;

/// <summary>One construct kept in plain arrays; the source for mesher tests that need no world.</summary>
internal sealed class ArrayConstructSource : IConstructSource
{
    private readonly Dictionary<ChunkPos, uint[]> _chunks = [];

    public void Set(CellPos cell, uint value)
    {
        if (!_chunks.TryGetValue(cell.Chunk, out var chunk))
        {
            chunk = new uint[ConstructChunk.CellCount];
            _chunks.Add(cell.Chunk, chunk);
        }

        chunk[cell.LocalIndex] = value;
    }

    public uint[] SetChunk(ChunkPos position, uint[] blocks) => _chunks[position] = blocks;

    public void CollectConstructs(FrameRef frame, List<EntityId> into) => into.Add(TestContent.ConstructId);

    public bool TryGetPose(EntityId id, out ConstructPose pose)
    {
        pose = default;
        return false;
    }

    public void CollectChunks(EntityId id, List<ChunkPos> into)
    {
        into.AddRange(_chunks.Keys);
        into.Sort();
    }

    public bool TryGetChunkBlocks(EntityId id, ChunkPos chunk, out ReadOnlySpan<uint> blocks)
    {
        if (id == TestContent.ConstructId && _chunks.TryGetValue(chunk, out var array))
        {
            blocks = array;
            return true;
        }

        blocks = default;
        return false;
    }

    public BlockState GetBlock(EntityId id, CellPos cell) =>
        _chunks.TryGetValue(cell.Chunk, out var chunk) ? new BlockState(chunk[cell.LocalIndex]) : BlockState.Empty;
}
