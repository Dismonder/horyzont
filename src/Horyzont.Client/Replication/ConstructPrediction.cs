using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Replication;

/// <summary>
/// Visual overlay of the local player's own block edits on a remote client (K3-15 decision 10). It never touches the authoritative
/// mirror, collisions, entity ids or any allocator. An entry stays until the operation named by the accepted reply was seen (in either
/// order), a full state shows the predicted cell, the intent fails, or <see cref="DeadlineTicks"/> client ticks passed.
/// </summary>
public sealed class ConstructPrediction : IEditPrediction, IIntentReplyObserver, IDisposable
{
    /// <summary>Most overlay entries (the same as the pending intents).</summary>
    public const int MaxEntries = PendingIntentTracker.MaxPending;

    /// <summary>Hard limit of an entry's life, in client ticks (5 s at 60 Hz).</summary>
    public const int DeadlineTicks = PendingIntentTracker.DeadlineTicks;

    private const int SeenOps = 64;

    private readonly ConstructMirror _mirror;
    private readonly Func<long> _clock;
    private readonly List<Entry> _entries = new(MaxEntries);
    private readonly List<PredictedBlock> _view = new(MaxEntries);
    private readonly OpId[] _seen = new OpId[SeenOps];
    private int _seenNext;

    /// <summary>Initializes a new instance of the <see cref="ConstructPrediction"/> class.</summary>
    /// <param name="mirror">Mirror whose operations and states settle the overlay.</param>
    /// <param name="clock">Client tick counter (physics ticks of the tool node).</param>
    public ConstructPrediction(ConstructMirror mirror, Func<long> clock)
    {
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(clock);
        _mirror = mirror;
        _clock = clock;
        mirror.OperationObserved += NoteOperation;
        mirror.Changed += OnConstructChanged;
        mirror.Cleared += OnMirrorCleared;
    }

    /// <summary>Raised when the overlay changed (an entry was added or removed).</summary>
    public event Action? OverlayChanged;

    /// <summary>Gets the number of overlay entries.</summary>
    public int Count
    {
        get
        {
            Prune();
            return _entries.Count;
        }
    }

    /// <summary>Gets the overlay, oldest first; the list is reused and valid until the next call.</summary>
    public IReadOnlyList<PredictedBlock> Entries
    {
        get
        {
            Prune();
            _view.Clear();
            foreach (var entry in _entries)
            {
                _view.Add(entry.Block);
            }

            return _view;
        }
    }

    /// <inheritdoc />
    public void Begin(uint clientSeq, Intent intent, in ToolPreview preview)
    {
        Prune();
        if (_entries.Count >= MaxEntries)
        {
            return;
        }

        PredictedBlock? block = intent switch
        {
            PlaceBlockIntent place when preview.Kind == ToolPreviewKind.ConstructCell
                => new PredictedBlock(clientSeq, preview.Construct, preview.Cell, BlockState.Create(place.BlockTypeId, place.Orientation),
                    default, Quaternion.Identity, false),
            PlaceBlockIntent place when preview.Kind == ToolPreviewKind.NewConstructCell
                => new PredictedBlock(clientSeq, EntityId.None, preview.Cell, BlockState.Create(place.BlockTypeId, place.Orientation),
                    preview.Origin, preview.Rotation, false),
            RemoveBlockIntent remove => new PredictedBlock(clientSeq, remove.Construct, remove.Cell, BlockState.Empty, default,
                Quaternion.Identity, false),
            _ => null,
        };
        if (block is { } value)
        {
            _entries.Add(new Entry(value, _clock()));
            OverlayChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public void NoteReply(IntentReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var index = IndexOf(reply.ClientSeq);
        if (index < 0 || reply.Reason != RejectReason.None)
        {
            return;
        }

        var entry = _entries[index];
        entry.AppliedOp = reply.AppliedOpId;
        _entries[index] = entry;
    }

    /// <inheritdoc />
    public void Commit(uint clientSeq)
    {
        var index = IndexOf(clientSeq);
        if (index < 0)
        {
            return;
        }

        var entry = _entries[index];
        entry.Block = entry.Block with { Accepted = true };
        _entries[index] = entry;
        if (entry.AppliedOp != OpId.None && WasSeen(entry.AppliedOp))
        {
            RemoveAt(index);
        }
    }

    /// <inheritdoc />
    public void Revert(uint clientSeq)
    {
        var index = IndexOf(clientSeq);
        if (index >= 0)
        {
            RemoveAt(index);
        }
    }

    /// <summary>Unsubscribes from the mirror and drops the overlay.</summary>
    public void Dispose()
    {
        _mirror.OperationObserved -= NoteOperation;
        _mirror.Changed -= OnConstructChanged;
        _mirror.Cleared -= OnMirrorCleared;
        if (_entries.Count > 0)
        {
            _entries.Clear();
            OverlayChanged?.Invoke();
        }
    }

    private static bool Matches(BlockState actual, BlockState predicted) =>
        actual.TypeId == predicted.TypeId && (actual.IsEmpty || actual.Orientation == predicted.Orientation);

    private void NoteOperation(OpId id)
    {
        _seen[_seenNext] = id;
        _seenNext = (_seenNext + 1) % SeenOps;
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Block.Accepted && _entries[i].AppliedOp == id)
            {
                RemoveAt(i);
            }
        }
    }

    private void OnMirrorCleared()
    {
        // A new session numbers its operations again: the old ones must not settle new predictions.
        Array.Clear(_seen);
        _seenNext = 0;
    }

    private void OnConstructChanged(EntityId construct)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var block = _entries[i].Block;
            if (block.Accepted && block.Construct == construct && _mirror.TryGetConstruct(construct, out var value)
                && Matches(value.GetBlock(block.Cell), block.Block))
            {
                RemoveAt(i);
            }
        }
    }

    private bool WasSeen(OpId id)
    {
        for (var i = 0; i < SeenOps; i++)
        {
            if (_seen[i] == id)
            {
                return true;
            }
        }

        return false;
    }

    private int IndexOf(uint clientSeq)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Block.ClientSeq == clientSeq)
            {
                return i;
            }
        }

        return -1;
    }

    private void RemoveAt(int index)
    {
        _entries.RemoveAt(index);
        OverlayChanged?.Invoke();
    }

    private void Prune()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        var now = _clock();
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (now - _entries[i].Tick >= DeadlineTicks)
            {
                RemoveAt(i);
            }
        }
    }

    private struct Entry(PredictedBlock block, long tick)
    {
        public PredictedBlock Block = block;
        public long Tick = tick;
        public OpId AppliedOp;
    }
}
