using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Terrain;

/// <summary>
/// The bookkeeping of the terrain edits a remote client shows before the server confirms them, free of the engine. An edit leaves the
/// ledger when the server refuses it, when it times out or the session ends, or when the barrier of its operation arrives: the
/// acceptance of an intent alone is not enough, because the blocks of the edit arrive after the reply. A barrier that comes before
/// the reply is remembered. The engine rebuilds the blocks an edit touched from the edits that remain (<see cref="CollectTouching"/>).
/// </summary>
public sealed class EditPredictionLedger(TimeProvider time)
{
    /// <summary>Most edits waiting at once; the pending-intent tracker allows the same number.</summary>
    public const int MaxEdits = 16;

    private const int MaxEarlyBarriers = 64;

    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));
    private readonly SortedDictionary<uint, PredictedEdit> _edits = [];
    private readonly HashSet<OpId> _earlyBarriers = [];
    private readonly Queue<OpId> _earlyOrder = new();

    /// <summary>Gets the number of edits that wait for the server.</summary>
    public int Count => _edits.Count;

    /// <summary>Adds an edit that was just sent.</summary>
    /// <param name="edit">The edit.</param>
    /// <returns>False when too many edits wait already (the edit then must not be shown).</returns>
    public bool TryBegin(PredictedEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (_edits.Count >= MaxEdits || _edits.ContainsKey(edit.ClientSeq))
        {
            return false;
        }

        _edits.Add(edit.ClientSeq, edit);
        return true;
    }

    /// <summary>The server accepted an intent and says which operation it became.</summary>
    /// <param name="clientSeq">Intent sequence.</param>
    /// <param name="operation">The applied operation.</param>
    /// <param name="edit">The edit when it is known.</param>
    /// <returns>True when the barrier of the operation came before the reply: the edit is finished and left the ledger.</returns>
    public bool NoteAccepted(uint clientSeq, OpId operation, out PredictedEdit? edit)
    {
        if (!_edits.TryGetValue(clientSeq, out edit))
        {
            return false;
        }

        edit.Operation = operation;
        edit.AcceptedAt = _time.GetTimestamp();
        if (_earlyBarriers.Remove(operation))
        {
            _edits.Remove(clientSeq);
            return true;
        }

        return false;
    }

    /// <summary>The barrier of an operation arrived: its blocks are final.</summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The finished edit, or null when the reply was not seen yet or the operation is not ours.</returns>
    public PredictedEdit? Complete(OpId operation)
    {
        foreach (var edit in _edits.Values)
        {
            if (edit.Operation == operation)
            {
                _edits.Remove(edit.ClientSeq);
                return edit;
            }
        }

        if (_earlyBarriers.Add(operation))
        {
            _earlyOrder.Enqueue(operation);
            while (_earlyOrder.Count > MaxEarlyBarriers)
            {
                _earlyBarriers.Remove(_earlyOrder.Dequeue());
            }
        }

        return null;
    }

    /// <summary>Removes an edit that failed (refused, timed out, disconnected).</summary>
    /// <param name="clientSeq">Intent sequence.</param>
    /// <returns>The removed edit; null when unknown.</returns>
    public PredictedEdit? Remove(uint clientSeq) => _edits.Remove(clientSeq, out var edit) ? edit : null;

    /// <summary>Collects the edits that touch a block, in the order they were sent.</summary>
    /// <param name="key">LOD zero block.</param>
    /// <param name="into">Receives the edits; cleared first.</param>
    public void CollectTouching(TerrainBlockKey key, List<PredictedEdit> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        foreach (var edit in _edits.Values)
        {
            if (edit.Keys.Contains(key))
            {
                into.Add(edit);
            }
        }
    }

    /// <summary>Removes the accepted edits whose barrier did not come in time; the caller asks the server for a manifest.</summary>
    /// <param name="timeout">How long an accepted edit may wait for its barrier.</param>
    /// <param name="into">Receives the expired edits; cleared first.</param>
    public void TakeExpired(TimeSpan timeout, List<PredictedEdit> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        var now = _time.GetTimestamp();
        foreach (var edit in _edits.Values)
        {
            if (edit.IsAccepted && _time.GetElapsedTime(edit.AcceptedAt, now) >= timeout)
            {
                into.Add(edit);
            }
        }

        foreach (var edit in into)
        {
            _edits.Remove(edit.ClientSeq);
        }
    }

    /// <summary>Removes every edit (the session ended), newest first.</summary>
    /// <param name="into">Receives the removed edits, newest first; cleared first.</param>
    public void TakeAll(List<PredictedEdit> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        foreach (var edit in _edits.Values.Reverse())
        {
            into.Add(edit);
        }

        _edits.Clear();
        _earlyBarriers.Clear();
        _earlyOrder.Clear();
    }
}
