using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;

namespace Horyzont.Core.World.Constructs;

/// <summary>A construct made of 1 m cells. Mutated only by the operation applier and the world loader.</summary>
public sealed class Construct
{
    // A Dictionary, not a SortedDictionary: SortedDictionary lookups allocate, and GetBlock runs in every raycast step.
    // Ordered enumeration sorts the keys on demand (hashing, checkpoints).
    private readonly Dictionary<ChunkPos, ConstructChunk> _chunks = [];
    private readonly ChangeTracker _tracker;
    private CellPos _boundsMin;
    private CellPos _boundsMax;

    internal Construct(
        EntityId id, FrameRef parent, uint parentSeq, ConstructMode mode, Vector3 origin, Quaternion rotation, uint version,
        ChangeTracker tracker)
    {
        Id = id;
        Parent = parent;
        ParentSeq = parentSeq;
        Mode = mode;
        Origin = origin;
        Rotation = rotation;
        Version = version;
        _tracker = tracker;
    }

    /// <summary>Entity id of the construct.</summary>
    public EntityId Id { get; }

    /// <summary>Parent frame (in subproject 1 the planet frame).</summary>
    public FrameRef Parent { get; private set; }

    /// <summary>Number of reparent operations applied to the construct.</summary>
    public uint ParentSeq { get; private set; }

    /// <summary>Construct mode; <see cref="ConstructMode.Static"/> at creation.</summary>
    public ConstructMode Mode { get; private set; }

    /// <summary>Corner of cell (0,0,0) in the parent frame.</summary>
    public Vector3 Origin { get; private set; }

    /// <summary>Rotation from construct space to the parent frame.</summary>
    public Quaternion Rotation { get; private set; }

    /// <summary>Block data version, incremented by every block change.</summary>
    public uint Version { get; private set; }

    /// <summary>Number of non-empty cells.</summary>
    public int BlockCount { get; private set; }

    /// <summary>Number of non-empty chunks.</summary>
    public int ChunkCount => _chunks.Count;

    /// <summary>Lowest occupied cell bound (conservative: it may only grow until the world is reloaded).</summary>
    public CellPos BoundsMin => _boundsMin;

    /// <summary>Highest occupied cell bound (conservative: it may only grow until the world is reloaded).</summary>
    public CellPos BoundsMax => _boundsMax;

    /// <summary>Non-empty chunks in <see cref="ChunkPos"/> order.</summary>
    public IEnumerable<KeyValuePair<ChunkPos, ConstructChunk>> Chunks
    {
        get
        {
            var keys = new ChunkPos[_chunks.Count];
            _chunks.Keys.CopyTo(keys, 0);
            Array.Sort(keys);
            foreach (var key in keys)
            {
                yield return new KeyValuePair<ChunkPos, ConstructChunk>(key, _chunks[key]);
            }
        }
    }

    /// <summary>
    /// Enumerates the non-empty chunks without allocating, in no defined order. For readers whose result does not depend on the order and that run every
    /// tick (K6-33 <c>RestEnvironment</c>); <see cref="Chunks"/> sorts and allocates. Internal on purpose: never for hashing, checkpoints or the applier.
    /// </summary>
    /// <returns>The struct enumerator of the chunk dictionary.</returns>
    internal Dictionary<ChunkPos, ConstructChunk>.Enumerator EnumerateChunksUnordered() => _chunks.GetEnumerator();

    /// <summary>Looks up a chunk.</summary>
    /// <param name="position">Chunk position.</param>
    /// <param name="chunk">The chunk when present.</param>
    /// <returns>True when the chunk has blocks.</returns>
    public bool TryGetChunk(ChunkPos position, out ConstructChunk? chunk) => _chunks.TryGetValue(position, out chunk);

    /// <summary>State of a cell; empty outside the valid range.</summary>
    /// <param name="cell">The cell.</param>
    /// <returns>The state.</returns>
    public BlockState GetBlock(CellPos cell)
    {
        if (!cell.IsInBounds || !_chunks.TryGetValue(cell.Chunk, out var chunk))
        {
            return BlockState.Empty;
        }

        return chunk.Get(cell.LocalIndex);
    }

    /// <summary>Converts a point from construct space to the parent frame.</summary>
    /// <param name="local">Point in construct space (1 unit = 1 cell).</param>
    /// <returns>Point in the parent frame.</returns>
    public Vector3 LocalToParent(Vector3 local) => Origin + Vector3.Transform(local, Rotation);

    /// <summary>Converts a point from the parent frame to construct space.</summary>
    /// <param name="parent">Point in the parent frame.</param>
    /// <returns>Point in construct space.</returns>
    public Vector3 ParentToLocal(Vector3 parent) => Vector3.Transform(parent - Origin, Quaternion.Inverse(Rotation));

    /// <summary>Centre of a cell in the parent frame.</summary>
    /// <param name="cell">The cell.</param>
    /// <returns>Point in the parent frame.</returns>
    public Vector3 CellCenterToParent(CellPos cell) => LocalToParent(new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f));

    /// <summary>Cell containing a point of the parent frame.</summary>
    /// <param name="parent">Point in the parent frame.</param>
    /// <returns>The cell (possibly outside the valid range).</returns>
    public CellPos ParentToCell(Vector3 parent)
    {
        var local = ParentToLocal(parent);
        return new CellPos((int)MathF.Floor(local.X), (int)MathF.Floor(local.Y), (int)MathF.Floor(local.Z));
    }

    /// <summary>Sets a cell, bumps <see cref="Version"/>, drops empty chunks and marks the change for the checkpoint.</summary>
    internal BlockState SetBlock(CellPos cell, BlockState state)
    {
        var chunkPos = cell.Chunk;
        if (!_chunks.TryGetValue(chunkPos, out var chunk))
        {
            if (state.IsEmpty)
            {
                return BlockState.Empty;
            }

            chunk = new ConstructChunk();
            _chunks.Add(chunkPos, chunk);
        }

        var previous = chunk.Set(cell.LocalIndex, state);
        if (previous.IsEmpty && !state.IsEmpty)
        {
            BlockCount++;
            Grow(cell);
        }
        else if (!previous.IsEmpty && state.IsEmpty)
        {
            BlockCount--;
        }

        if (chunk.NonEmptyCount == 0)
        {
            _chunks.Remove(chunkPos);
        }

        Version++;
        _tracker.MarkChunk(new ConstructChunkKey(Id, chunkPos));
        _tracker.MarkConstruct(Id);
        return previous;
    }

    /// <summary>
    /// Sets the function state (bits 25-27) of a block without changing <see cref="Version"/>; a compound operation bumps the version once
    /// with <see cref="BumpVersion"/>. The caller checked that the cell holds a block.
    /// </summary>
    internal void ChangeFunctionState(CellPos cell, byte functionState)
    {
        var chunkPos = cell.Chunk;
        var chunk = _chunks[chunkPos];
        chunk.Set(cell.LocalIndex, chunk.Get(cell.LocalIndex).WithFunctionState(functionState));
        _tracker.MarkChunk(new ConstructChunkKey(Id, chunkPos));
        _tracker.MarkConstruct(Id);
    }

    /// <summary>Raises <see cref="Version"/> by one and marks the header changed.</summary>
    internal void BumpVersion()
    {
        Version++;
        _tracker.MarkConstruct(Id);
    }

    /// <summary>Moves the construct to a new parent frame.</summary>
    internal void Reparent(FrameRef parent, uint parentSeq, Vector3 origin, Quaternion rotation)
    {
        Parent = parent;
        ParentSeq = parentSeq;
        Origin = origin;
        Rotation = rotation;
        _tracker.MarkConstruct(Id);
    }

    /// <summary>
    /// Moves a ship-mode construct within its parent frame outside the journal (K5-20, <c>ServerSimulation.UpdateConstructKinematics</c>);
    /// marks the header changed so the checkpoint stores the pose.
    /// </summary>
    internal void SetPose(Vector3 origin, Quaternion rotation)
    {
        Origin = origin;
        Rotation = rotation;
        _tracker.MarkConstruct(Id);
    }

    /// <summary>
    /// Switches the construct mode and sets the pose in the same parent frame. Bumps <see cref="Version"/> by one, like a block
    /// operation, so observers with a live link receive the operation instead of a retransfer.
    /// </summary>
    internal void SetMode(ConstructMode mode, Vector3 origin, Quaternion rotation)
    {
        Mode = mode;
        Origin = origin;
        Rotation = rotation;
        Version++;
        _tracker.MarkConstruct(Id);
    }

    /// <summary>Loads a chunk without marking changes (world loader).</summary>
    internal void LoadChunk(ChunkPos position, ReadOnlySpan<uint> blocks)
    {
        if (blocks.Length != ConstructChunk.CellCount)
        {
            throw new InvalidDataException($"Chunk {position} of construct {Id.Value} has {blocks.Length} cells.");
        }

        if (_chunks.ContainsKey(position))
        {
            throw new InvalidDataException($"Duplicate chunk {position} of construct {Id.Value}.");
        }

        var chunk = new ConstructChunk();
        for (var i = 0; i < blocks.Length; i++)
        {
            var state = new BlockState(blocks[i]);
            if (!state.IsWellFormed)
            {
                throw new InvalidDataException($"Malformed block 0x{blocks[i]:X8} in chunk {position} of construct {Id.Value}.");
            }

            if (!state.IsEmpty)
            {
                chunk.Set(i, state);
                BlockCount++;
                Grow(CellPos.FromChunk(position, i));
            }
        }

        if (chunk.NonEmptyCount > 0)
        {
            _chunks.Add(position, chunk);
        }
    }

    private void Grow(CellPos cell)
    {
        if (BlockCount == 1)
        {
            _boundsMin = cell;
            _boundsMax = cell;
            return;
        }

        _boundsMin = new CellPos(Math.Min(_boundsMin.X, cell.X), Math.Min(_boundsMin.Y, cell.Y), Math.Min(_boundsMin.Z, cell.Z));
        _boundsMax = new CellPos(Math.Max(_boundsMax.X, cell.X), Math.Max(_boundsMax.Y, cell.Y), Math.Max(_boundsMax.Z, cell.Z));
    }
}
