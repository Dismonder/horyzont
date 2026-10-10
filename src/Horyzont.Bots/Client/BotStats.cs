using Horyzont.Bots.Scripting;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;

namespace Horyzont.Bots.Client;

/// <summary>One answered intent: what the bot sent, when, and what the server said (timestamps of the bot's <see cref="TimeProvider"/>).</summary>
/// <param name="ClientSeq">Client sequence number.</param>
/// <param name="Kind">The script action behind the intent.</param>
/// <param name="SentAt">Timestamp of the submit.</param>
/// <param name="RepliedAt">Timestamp of the reply.</param>
/// <param name="Reason">The verdict; <see cref="RejectReason.None"/> when accepted.</param>
/// <param name="AppliedOpId">The operation of an accepted intent; <see cref="OpId.None"/> otherwise.</param>
public readonly record struct IntentRecord(uint ClientSeq, BotAction Kind, long SentAt, long RepliedAt, RejectReason Reason, OpId AppliedOpId);

/// <summary>Counters and the intent log of one bot.</summary>
public sealed class BotStats
{
    private readonly Dictionary<RejectReason, int> _rejections = [];
    private readonly List<IntentRecord> _records = [];

    /// <summary>Gets the number of intents sent.</summary>
    public int IntentsSent { get; internal set; }

    /// <summary>Gets the number of intents the server accepted.</summary>
    public int Accepted { get; private set; }

    /// <summary>Gets the number of intents the server refused.</summary>
    public int Rejected { get; private set; }

    /// <summary>Gets the number of replies for intents the bot did not know.</summary>
    public int UnknownReplies { get; internal set; }

    /// <summary>Gets the number of abuse attempts the client refused to send (not finite values).</summary>
    public int RefusedLocally { get; internal set; }

    /// <summary>Gets the number of connections that ended.</summary>
    public int Disconnects { get; internal set; }

    /// <summary>Gets the number of connection attempts after the first one.</summary>
    public int Reconnects { get; internal set; }

    /// <summary>Gets the number of times the bot reached the game state.</summary>
    public int Joins { get; internal set; }

    /// <summary>Gets the number of own-state snapshots accepted.</summary>
    public int Snapshots { get; internal set; }

    /// <summary>Gets the number of inputs recorded.</summary>
    public int Inputs { get; internal set; }

    /// <summary>Gets the rejections by reason.</summary>
    public IReadOnlyDictionary<RejectReason, int> Rejections => _rejections;

    /// <summary>Gets the answered intents in the order of the replies.</summary>
    public IReadOnlyList<IntentRecord> Records => _records;

    internal void Add(in IntentRecord record)
    {
        _records.Add(record);
        if (record.Reason == RejectReason.None)
        {
            Accepted++;
            return;
        }

        Rejected++;
        _rejections[record.Reason] = _rejections.GetValueOrDefault(record.Reason) + 1;
    }
}
