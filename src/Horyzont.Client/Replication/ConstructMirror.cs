using System.Diagnostics.CodeAnalysis;

using Horyzont.Constructs.Sources;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Replication;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Replication;

/// <summary>Client construct copies, published only after a complete ordered transfer.</summary>
public sealed class ConstructMirror(TimeProvider time) : IConstructSource
{
    private const int MaxStagingBytes = 16 * 1024 * 1024;
    private readonly ConstructReplicaSet _set = new();
    private readonly Dictionary<EntityId, Transfer> _transfers = [];
    private readonly HashSet<EntityId> _resync = [];
    private readonly List<EntityId> _expired = [];
    private int _invalid;

    /// <summary>Raised once per unresolved entity gap.</summary>
    public event Action<EntityId>? ResyncNeeded;

    /// <summary>Raised for every operation message, applied or not, after it was handled (edit predictions wait for their operation).</summary>
    public event Action<OpId>? OperationObserved;

    /// <summary>Raised when a copy changes or is withdrawn (any reason).</summary>
    public event Action<EntityId>? Changed;

    /// <summary>Raised when a complete full state was published. The view builds the whole construct.</summary>
    public event Action<EntityId>? StatePublished;

    /// <summary>Raised when a published copy is withdrawn: a new state starts, the construct was removed or repaired. The view unloads it.</summary>
    public event Action<EntityId>? StateWithdrawn;

    /// <summary>Raised after an operation changed a published copy (never for a removal, which raises <see cref="StateWithdrawn"/>).</summary>
    public event Action<Operation, IReadOnlyList<ConstructChange>>? EditApplied;

    /// <summary>Gets the read-only store of the published copies, for ray, overlap and pose queries.</summary>
    public ConstructStore Store => _set.Store;

    /// <summary>Bytes currently retained in incomplete transfers.</summary>
    public int StagingBytes { get; private set; }

    /// <summary>Applies an ordered part; true only when a complete copy is published.</summary>
    /// <param name="part">Full state part.</param>
    /// <returns>Whether a complete copy was published.</returns>
    public bool Apply(ConstructState part)
    {
        ArgumentNullException.ThrowIfNull(part);
        ExpireTransfers();
        var id = part.Header.Id;
        if (part.PartCount is 0 or > 4096 || part.PartIndex >= part.PartCount || part.Chunks.Count > 12)
        {
            return Invalid(id);
        }

        if (part.PartIndex == 0)
        {
            DropTransfer(id);
            Withdraw(id);
            _transfers[id] = new Transfer(part.Header, part.PartCount, time.GetTimestamp());
        }

        if (!_transfers.TryGetValue(id, out var transfer) || transfer.Header != part.Header
            || transfer.Count != part.PartCount || transfer.Next != part.PartIndex)
        {
            return Invalid(id);
        }

        foreach (var chunk in part.Chunks)
        {
            if (chunk.Key.Construct != id || chunk.Blocks.Length != 4096 || transfer.Chunks.Count >= 4096
                || StagingBytes + 16384 > MaxStagingBytes || !transfer.Keys.Add(chunk.Key.Chunk))
            {
                return Invalid(id);
            }

            // Own the bytes even when callers bypass the wire decoder.
            transfer.Chunks.Add(new ConstructChunkRecord(chunk.Key, (uint[])chunk.Blocks.Clone()));
            StagingBytes += 16384;
        }

        transfer.Next++;
        if (transfer.Next != transfer.Count)
        {
            return false;
        }

        try
        {
            _set.Add(transfer.Header, transfer.Chunks);
        }
        catch (InvalidDataException)
        {
            // Invalid copies are never published; the first fault requests repair, repeated faults disconnect.
            return Invalid(id);
        }

        DropTransfer(id);
        _resync.Remove(id);
        StatePublished?.Invoke(id);
        Changed?.Invoke(id);
        return true;
    }

    /// <summary>Applies visible operation effects with per-construct version checks; global operation gaps are legal.</summary>
    /// <param name="message">Visible operation.</param>
    /// <returns>Whether a copy changed.</returns>
    public bool Apply(OperationApplied message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var changed = ApplyOperation(message);
        OperationObserved?.Invoke(message.Id);
        return changed;
    }

    private bool ApplyOperation(OperationApplied message)
    {
        if (message.Operation is EntityReparentOp reparent)
        {
            if (_set.TryGet(reparent.Entity, out var target)
                && target.TryReparent(reparent.NewParent, reparent.NewParentSeq, reparent.LocalPosition, reparent.LocalRotation))
            {
                EditApplied?.Invoke(message.Operation, message.ConstructChanges);
                Changed?.Invoke(reparent.Entity);
                return true;
            }

            return false;
        }

        if (message.ConstructChanges.Count != 1)
        {
            foreach (var change in message.ConstructChanges)
            {
                RequestResync(change.Construct);
            }

            return false;
        }

        var result = message.ConstructChanges[0];
        var id = result.Construct;
        if (_resync.Contains(id) || _transfers.ContainsKey(id))
        {
            RequestResync(id);
            return false;
        }

        if (message.Operation is ConstructCreateOp create)
        {
            if (create.Construct != id || result.Version != 1 || result.Removed || _set.TryGet(id, out _))
            {
                return false;
            }

            var created = _set.Add(new ConstructHeaderRecord(id, create.Parent, 0, ConstructMode.Static, create.Origin, create.Rotation, 0), []);
            if (!created.TrySet(create.FirstCell, create.Block, 1))
            {
                _set.Remove(id);
                RequestResync(id);
                return false;
            }
        }
        else
        {
            if (!_set.TryGet(id, out var replica))
            {
                RequestResync(id);
                return false;
            }

            if (!IsNewer(result.Version, replica.Value.Version))
            {
                return false;
            }

            var applied = message.Operation switch
            {
                BlockSetOp set when set.Construct == id && !result.Removed => replica.TrySet(set.Cell, set.Block, result.Version),
                BlockClearOp clear when clear.Construct == id => replica.TrySet(clear.Cell, BlockState.Empty, result.Version),
                _ => false,
            };
            if (!applied)
            {
                RequestResync(id);
                return false;
            }

            if (result.Removed)
            {
                Remove(id);
                return true;
            }
        }

        EditApplied?.Invoke(message.Operation, message.ConstructChanges);
        Changed?.Invoke(id);
        return true;
    }

    /// <summary>Withdraws an entity and releases its staging bytes.</summary>
    /// <param name="id">Entity.</param>
    public void Remove(EntityId id)
    {
        DropTransfer(id);
        _resync.Remove(id);
        Withdraw(id);
    }

    /// <summary>Releases all session data.</summary>
    public void Clear()
    {
        _set.Clear();
        _transfers.Clear();
        _resync.Clear();
        StagingBytes = 0;
        _invalid = 0;
    }

    /// <summary>Requests repair for transfers unfinished after five seconds.</summary>
    public void ExpireTransfers()
    {
        _expired.Clear();
        foreach (var pair in _transfers)
        {
            if (time.GetElapsedTime(pair.Value.Start) >= TimeSpan.FromSeconds(5))
            {
                _expired.Add(pair.Key);
            }
        }

        foreach (var id in _expired)
        {
            DropTransfer(id);
            RequestResync(id);
        }
    }

    /// <summary>Looks up the published read-only construct.</summary>
    /// <param name="id">Entity.</param>
    /// <param name="value">Published copy.</param>
    /// <returns>Whether the complete copy is available.</returns>
    public bool TryGetConstruct(EntityId id, [NotNullWhen(true)] out Construct? value)
    {
        value = _set.TryGet(id, out var replica) ? replica.Value : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void CollectConstructs(FrameRef frame, List<EntityId> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var all = _set.Store.All;
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
        if (TryGetConstruct(id, out var value))
        {
            pose = new ConstructPose(id, value.Parent, value.Origin, value.Rotation, value.Version, value.BoundsMin, value.BoundsMax);
            return true;
        }

        pose = default;
        return false;
    }

    /// <inheritdoc />
    public void CollectChunks(EntityId id, List<ChunkPos> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (TryGetConstruct(id, out var value))
        {
            foreach (var chunk in value.Chunks)
            {
                into.Add(chunk.Key);
            }
        }
    }

    /// <inheritdoc />
    public bool TryGetChunkBlocks(EntityId id, ChunkPos chunk, out ReadOnlySpan<uint> blocks)
    {
        if (TryGetConstruct(id, out var value) && value.TryGetChunk(chunk, out var data))
        {
            blocks = data!.Blocks;
            return true;
        }

        blocks = default;
        return false;
    }

    /// <inheritdoc />
    public BlockState GetBlock(EntityId id, CellPos cell) => TryGetConstruct(id, out var value) ? value.GetBlock(cell) : default;

    private void Withdraw(EntityId id)
    {
        if (_set.Remove(id))
        {
            StateWithdrawn?.Invoke(id);
            Changed?.Invoke(id);
        }
    }

    private static bool IsNewer(uint value, uint previous) => unchecked((int)(value - previous)) > 0;

    private bool Invalid(EntityId id)
    {
        DropTransfer(id);
        RequestResync(id);
        if (++_invalid > 8)
        {
            throw new Horyzont.Core.Serialization.ProtocolViolationException("Repeated invalid construct transfers.");
        }

        return false;
    }

    private void RequestResync(EntityId id)
    {
        if (!id.IsNone && _resync.Add(id))
        {
            ResyncNeeded?.Invoke(id);
        }
    }

    private void DropTransfer(EntityId id)
    {
        if (_transfers.Remove(id, out var transfer))
        {
            StagingBytes -= transfer.Chunks.Count * 16384;
        }
    }

    private sealed class Transfer(ConstructHeaderRecord header, ushort count, long start)
    {
        public ConstructHeaderRecord Header { get; } = header;
        public ushort Count { get; } = count;
        public long Start { get; } = start;
        public int Next { get; set; }
        public List<ConstructChunkRecord> Chunks { get; } = [];
        public HashSet<ChunkPos> Keys { get; } = [];
    }
}
