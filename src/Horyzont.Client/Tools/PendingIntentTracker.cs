using Horyzont.Core.Intents;
using Horyzont.Core.Protocol.Messages;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Tools;

/// <summary>
/// Intents sent and not yet answered. At most <see cref="MaxPending"/> wait at the same time and each waits at most
/// <see cref="DeadlineTicks"/>; an expired entry is reverted and logged. The entries are a list ordered by client sequence number
/// (not a dictionary), because the order of reverting is part of the contract: from the newest to the oldest.
/// </summary>
public sealed class PendingIntentTracker
{
    /// <summary>Largest number of intents that wait for the server.</summary>
    public const int MaxPending = 16;

    /// <summary>Ticks after which an unanswered intent is given up: 5 s at 60 Hz, half of the idle limit of the session.</summary>
    public const int DeadlineTicks = 300;

    private readonly IEditPrediction _prediction;
    private readonly ILogger<PendingIntentTracker> _logger;
    private readonly List<PendingIntent> _entries = new(MaxPending);

    /// <summary>Initializes a new instance of the <see cref="PendingIntentTracker"/> class.</summary>
    /// <param name="prediction">Prediction that is committed or reverted.</param>
    /// <param name="logger">Logger.</param>
    public PendingIntentTracker(IEditPrediction prediction, ILogger<PendingIntentTracker> logger)
    {
        _prediction = prediction ?? throw new ArgumentNullException(nameof(prediction));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Gets a value indicating whether <see cref="MaxPending"/> intents wait: the next action is refused locally.</summary>
    public bool IsFull => _entries.Count >= MaxPending;

    /// <summary>Gets the number of waiting intents.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets the reason of the last rejection by the server (for the HUD); <see cref="RejectReason.None"/> before any.</summary>
    public RejectReason LastRejection { get; private set; }

    /// <summary>Gets the outcome of the latest finished intent, or <c>null</c> before any.</summary>
    public PendingIntentOutcome? LastOutcome { get; private set; }

    /// <summary>Gets the client sequence number of the latest finished intent.</summary>
    public uint LastOutcomeSeq { get; private set; }

    /// <summary>Shows a refusal that the client itself made without sending anything (the HUD reads it like a refusal of the server).</summary>
    /// <param name="reason">The reason.</param>
    public void NoteLocalRefusal(RejectReason reason) => LastRejection = reason;

    /// <summary>Starts waiting for the reply of an intent that was just sent.</summary>
    /// <param name="clientSeq">Client sequence number, higher than all waiting ones.</param>
    /// <param name="intent">The intent.</param>
    /// <param name="preview">Preview at the time of sending.</param>
    /// <param name="clientTick">Client tick of sending.</param>
    /// <exception cref="InvalidOperationException">The tracker is full or the sequence number is not increasing (a bug of the caller).</exception>
    public void Add(uint clientSeq, Intent intent, in ToolPreview preview, long clientTick)
    {
        if (IsFull)
        {
            throw new InvalidOperationException($"More than {MaxPending} pending intents; check IsFull before sending.");
        }

        if (_entries.Count > 0 && _entries[^1].ClientSeq >= clientSeq)
        {
            throw new InvalidOperationException($"Client sequence {clientSeq} does not follow {_entries[^1].ClientSeq}.");
        }

        _entries.Add(new PendingIntent(clientSeq, intent, preview, clientTick));
        _prediction.Begin(clientSeq, intent, in preview);
    }

    /// <summary>Settles an intent with the reply of the server.</summary>
    /// <param name="reply">The reply.</param>
    /// <param name="outcome">Accepted or Rejected when the reply belonged to a waiting intent.</param>
    /// <returns>False for an unknown or repeated <c>ClientSeq</c>: logged and ignored, never throws.</returns>
    public bool OnReply(IntentReply reply, out PendingIntentOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(reply);
        outcome = default;
        var index = IndexOf(reply.ClientSeq);
        if (index < 0)
        {
            _logger.UnknownReply(reply.ClientSeq, reply.Reason);
            return false;
        }

        _entries.RemoveAt(index);
        if (reply.Reason == RejectReason.None)
        {
            outcome = PendingIntentOutcome.Accepted;
            _prediction.Commit(reply.ClientSeq);
        }
        else
        {
            outcome = PendingIntentOutcome.Rejected;
            LastRejection = reply.Reason;
            _logger.IntentRejected(reply.ClientSeq, reply.Reason);
            _prediction.Revert(reply.ClientSeq);
        }

        Finish(reply.ClientSeq, outcome);
        return true;
    }

    /// <summary>Gives up the intents older than <see cref="DeadlineTicks"/>, from the newest to the oldest.</summary>
    /// <param name="clientTick">Current client tick.</param>
    public void Tick(long clientTick)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            var age = clientTick - entry.AddedTick;
            if (age < DeadlineTicks)
            {
                continue;
            }

            _entries.RemoveAt(i);
            _logger.IntentTimedOut(entry.ClientSeq, age);
            _prediction.Revert(entry.ClientSeq);
            Finish(entry.ClientSeq, PendingIntentOutcome.TimedOut);
        }
    }

    /// <summary>The connection ended: reverts every waiting intent, from the newest to the oldest.</summary>
    public void Clear()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        _logger.PendingCleared(_entries.Count);
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var seq = _entries[i].ClientSeq;
            _entries.RemoveAt(i);
            _prediction.Revert(seq);
            Finish(seq, PendingIntentOutcome.Disconnected);
        }
    }

    private int IndexOf(uint clientSeq)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].ClientSeq == clientSeq)
            {
                return i;
            }
        }

        return -1;
    }

    private void Finish(uint clientSeq, PendingIntentOutcome outcome)
    {
        LastOutcome = outcome;
        LastOutcomeSeq = clientSeq;
    }
}
