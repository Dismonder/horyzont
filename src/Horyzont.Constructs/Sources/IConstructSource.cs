using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Constructs.Sources;

/// <summary>
/// Read side of constructs for meshing and tools. Main thread only. Implemented over <c>WorldState</c> (solo, host,
/// dedicated server) here and over the client mirror in K3-15.
/// </summary>
public interface IConstructSource
{
    /// <summary>Appends the constructs whose parent is the frame, sorted by entity id.</summary>
    /// <param name="frame">Parent frame.</param>
    /// <param name="into">List to append to.</param>
    void CollectConstructs(FrameRef frame, List<EntityId> into);

    /// <summary>Pose of a construct.</summary>
    /// <param name="id">Construct entity.</param>
    /// <param name="pose">The pose when the construct exists.</param>
    /// <returns>True when the construct exists.</returns>
    bool TryGetPose(EntityId id, out ConstructPose pose);

    /// <summary>Appends the non-empty chunks of a construct, sorted.</summary>
    /// <param name="id">Construct entity.</param>
    /// <param name="into">List to append to.</param>
    void CollectChunks(EntityId id, List<ChunkPos> into);

    /// <summary>Raw blocks of a chunk in <see cref="CellPos.LocalIndex"/> order.</summary>
    /// <param name="id">Construct entity.</param>
    /// <param name="chunk">Chunk position.</param>
    /// <param name="blocks">The blocks when the chunk is non-empty.</param>
    /// <returns>True when the chunk exists.</returns>
    bool TryGetChunkBlocks(EntityId id, ChunkPos chunk, out ReadOnlySpan<uint> blocks);

    /// <summary>State of a cell.</summary>
    /// <param name="id">Construct entity.</param>
    /// <param name="cell">The cell.</param>
    /// <returns>The state; empty when absent.</returns>
    BlockState GetBlock(EntityId id, CellPos cell);
}
