using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Constructs.Sources;

/// <summary>
/// <see cref="IConstructSource"/> over the live <see cref="WorldState"/> (solo, host, dedicated server). Main thread only: the
/// simulation runs on the same thread, so reads need no lock.
/// </summary>
/// <param name="state">The world.</param>
public sealed class WorldStateConstructSource(WorldState state) : IConstructSource
{
    private readonly WorldState _state = state ?? throw new ArgumentNullException(nameof(state));

    /// <inheritdoc />
    public void CollectConstructs(FrameRef frame, List<EntityId> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var all = _state.Constructs.All;
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Parent == frame)
            {
                into.Add(all[i].Id);
            }
        }
    }

    /// <inheritdoc />
    public bool TryGetPose(EntityId id, out ConstructPose pose)
    {
        if (_state.Constructs.TryGet(id, out var construct))
        {
            pose = new ConstructPose(
                construct.Id, construct.Parent, construct.Origin, construct.Rotation, construct.Version, construct.BoundsMin,
                construct.BoundsMax);
            return true;
        }

        pose = default;
        return false;
    }

    /// <summary>
    /// Appends the non-empty chunks in order. <c>Construct.Chunks</c> allocates a sorted array per enumeration (Core is out of scope
    /// for this package), so this path is meant for <c>RebuildAll</c> and the creation of a construct view, not for every frame.
    /// </summary>
    /// <param name="id">Construct entity.</param>
    /// <param name="into">List to append to.</param>
    public void CollectChunks(EntityId id, List<ChunkPos> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (!_state.Constructs.TryGet(id, out var construct))
        {
            return;
        }

        foreach (var chunk in construct.Chunks)
        {
            into.Add(chunk.Key);
        }
    }

    /// <inheritdoc />
    public bool TryGetChunkBlocks(EntityId id, ChunkPos chunk, out ReadOnlySpan<uint> blocks)
    {
        if (_state.Constructs.TryGet(id, out var construct) && construct.TryGetChunk(chunk, out var data) && data is not null)
        {
            blocks = data.Blocks;
            return true;
        }

        blocks = default;
        return false;
    }

    /// <inheritdoc />
    public BlockState GetBlock(EntityId id, CellPos cell) =>
        _state.Constructs.TryGet(id, out var construct) ? construct.GetBlock(cell) : BlockState.Empty;
}
