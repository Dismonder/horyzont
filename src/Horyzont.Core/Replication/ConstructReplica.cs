using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Replication;

/// <summary>An isolated client copy of a construct, with sequential mutation checks and no world allocator or journal.</summary>
public sealed class ConstructReplica
{
    private readonly ChangeTracker _tracker;

    private ConstructReplica(Construct value, ChangeTracker tracker)
    {
        Value = value;
        _tracker = tracker;
    }

    /// <summary>The read-only public construct view.</summary>
    public Construct Value { get; }

    /// <summary>Copies a complete header and chunk set without modifying authoritative state.</summary>
    /// <param name="header">Construct header.</param>
    /// <param name="chunks">Unique chunks owned by this construct.</param>
    /// <returns>The isolated replica.</returns>
    public static ConstructReplica Create(ConstructHeaderRecord header, IReadOnlyList<ConstructChunkRecord> chunks)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(chunks);
        if (header.Id.IsNone || header.Parent.Id == 0 || header.Parent.Kind is < FrameKind.System or > FrameKind.Construct
            || header.Mode is < ConstructMode.Static or > ConstructMode.Ship
            || !Geometry.IsFinite(header.Origin) || !Geometry.IsRotation(header.Rotation))
        {
            throw new InvalidDataException("Invalid replica header.");
        }

        var tracker = new ChangeTracker();
        var value = new Construct(header.Id, header.Parent, header.ParentSeq, header.Mode, header.Origin, header.Rotation, header.Version, tracker);
        var seen = new HashSet<ChunkPos>();
        foreach (var chunk in chunks)
        {
            if (chunk.Key.Construct != header.Id || !seen.Add(chunk.Key.Chunk))
            {
                throw new InvalidDataException("Duplicate or foreign replica chunk.");
            }

            value.LoadChunk(chunk.Key.Chunk, chunk.Blocks);
        }

        return new ConstructReplica(value, tracker);
    }

    /// <summary>Applies exactly the next block version; false leaves the copy unchanged.</summary>
    /// <param name="cell">Target cell.</param>
    /// <param name="state">Well-formed block or empty value.</param>
    /// <param name="version">Next version.</param>
    /// <returns>Whether the mutation was applied.</returns>
    public bool TrySet(CellPos cell, BlockState state, uint version)
    {
        if (version != unchecked(Value.Version + 1) || !cell.IsInBounds || !state.IsWellFormed
            || (state.IsEmpty && Value.GetBlock(cell).IsEmpty))
        {
            return false;
        }

        Value.SetBlock(cell, state);
        _tracker.Clear();
        return true;
    }

    /// <summary>Applies the next reparent counter without changing the block version.</summary>
    /// <param name="parent">New parent.</param>
    /// <param name="parentSeq">Next counter.</param>
    /// <param name="origin">Local origin.</param>
    /// <param name="rotation">Local rotation.</param>
    /// <returns>Whether the reparent was applied.</returns>
    public bool TryReparent(FrameRef parent, uint parentSeq, Vector3 origin, Quaternion rotation)
    {
        if (parentSeq != unchecked(Value.ParentSeq + 1) || parent.Id == 0
            || parent.Kind is < FrameKind.System or > FrameKind.Construct || !Geometry.IsFinite(origin) || !Geometry.IsRotation(rotation))
        {
            return false;
        }

        Value.Reparent(parent, parentSeq, origin, rotation);
        _tracker.Clear();
        return true;
    }
}
