using Horyzont.Core.Ids;
using Horyzont.Core.Intents;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Result of an intent.</summary>
/// <param name="ClientSeq">Sequence number of the intent.</param>
/// <param name="Reason">Reject reason or <see cref="RejectReason.None"/>.</param>
/// <param name="AppliedOpId">Applied operation; <see cref="OpId.None"/> when rejected.</param>
public sealed record IntentReply(uint ClientSeq, RejectReason Reason, OpId AppliedOpId) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.IntentReply;
}
