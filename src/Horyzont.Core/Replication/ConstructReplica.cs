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
        Validate(header);
        var tracker = new ChangeTracker();
        var value = new Construct(header.Id, header.Parent, header.ParentSeq, header.Mode, header.Origin, header.Rotation, header.Version, tracker);
        LoadChunks(value, header, chunks);
        return new ConstructReplica(value, tracker);
    }

    /// <summary>Adds a replica to a private store, so the public read-only ray and overlap queries can use the copy.</summary>
    /// <param name="store">Store of the replica set.</param>
    /// <param name="tracker">Tracker shared by the store; cleared after loading.</param>
    /// <param name="header">Construct header.</param>
    /// <param name="chunks">Unique chunks owned by this construct.</param>
    /// <returns>The replica, already in <paramref name="store"/>.</returns>
    internal static ConstructReplica LoadInto(
        ConstructStore store, ChangeTracker tracker, ConstructHeaderRecord header, IReadOnlyList<ConstructChunkRecord> chunks)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(chunks);
        Validate(header);
        var value = store.Load(header.Id, header.Parent, header.ParentSeq, header.Mode, header.Origin, header.Rotation, header.Version);
        try
        {
            LoadChunks(value, header, chunks);
        }
        catch (InvalidDataException)
        {
            // Never leave a partly loaded copy in the store.
            store.Remove(header.Id);
            tracker.Clear();
            throw;
        }

        tracker.Clear();
        return new ConstructReplica(value, tracker);
    }

    private static void Validate(ConstructHeaderRecord header)
    {
        if (header.Id.IsNone || header.Parent.Id == 0 || header.Parent.Kind is < FrameKind.System or > FrameKind.Construct
            || header.Mode is < ConstructMode.Static or > ConstructMode.Ship
            || !Geometry.IsFinite(header.Origin) || !Geometry.IsRotation(header.Rotation))
        {
            throw new InvalidDataException("Invalid replica header.");
        }
    }

    private static void LoadChunks(Construct value, ConstructHeaderRecord header, IReadOnlyList<ConstructChunkRecord> chunks)
    {
        var seen = new HashSet<ChunkPos>();
        foreach (var chunk in chunks)
        {
            if (chunk.Key.Construct != header.Id || !seen.Add(chunk.Key.Chunk))
            {
                throw new InvalidDataException("Duplicate or foreign replica chunk.");
            }

            value.LoadChunk(chunk.Key.Chunk, chunk.Blocks);
        }
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

    /// <summary>Applies exactly the next version of a function state change (K6-30): bits 25-27 of the block in the cell.</summary>
    /// <param name="cell">Target cell; must hold a block of <paramref name="expectedType"/>.</param>
    /// <param name="expectedType">Block type the cell must hold.</param>
    /// <param name="state">New function state, 0..7.</param>
    /// <param name="version">Next version.</param>
    /// <returns>Whether the change was applied; false leaves the copy unchanged.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Skeleton of K6-30 A6; the behaviour reads the instance.")]
    public bool TrySetFunctionState(CellPos cell, ushort expectedType, byte state, uint version)
    {
        // K6-30 A6: the behaviour.
        _ = (cell, expectedType, state, version);
        return false;
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

    /// <summary>Applies the next block version of a mode switch (K5-23): the mode and pose change, the parent and its counter do not.</summary>
    /// <param name="mode">New mode; differs from the current one.</param>
    /// <param name="parent">Parent the operation was resolved against; must equal the current one.</param>
    /// <param name="parentSeq">Reparent counter the operation was resolved against; must equal the current one.</param>
    /// <param name="origin">Pose position after the switch.</param>
    /// <param name="rotation">Pose rotation after the switch.</param>
    /// <param name="version">Next version.</param>
    /// <returns>Whether the switch was applied; false leaves the copy unchanged.</returns>
    public bool TrySetMode(ConstructMode mode, FrameRef parent, uint parentSeq, Vector3 origin, Quaternion rotation, uint version)
    {
        if (version != unchecked(Value.Version + 1) || mode is not (ConstructMode.Static or ConstructMode.Ship) || mode == Value.Mode
            || parent != Value.Parent || parentSeq != Value.ParentSeq || !Geometry.IsFinite(origin) || !Geometry.IsRotation(rotation))
        {
            return false;
        }

        Value.SetMode(mode, origin, rotation);
        _tracker.Clear();
        return true;
    }
}
