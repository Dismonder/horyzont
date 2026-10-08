namespace Horyzont.Core.Protocol.Messages;

/// <summary>Round-trip probe.</summary>
/// <param name="ClientTimeMicros">Client clock in microseconds.</param>
public sealed record Ping(ulong ClientTimeMicros) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.Ping;
}
