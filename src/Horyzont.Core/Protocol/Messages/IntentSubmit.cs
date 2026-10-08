using Horyzont.Core.Intents;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Client submits an intent.</summary>
/// <param name="ClientSeq">Client sequence number echoed by the reply.</param>
/// <param name="Intent">The intent.</param>
public sealed record IntentSubmit(uint ClientSeq, Intent Intent) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.IntentSubmit;
}
