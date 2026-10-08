namespace Horyzont.Core.Protocol.Messages;

/// <summary>Server refuses a client. The format is frozen forever.</summary>
/// <param name="Code">Reason.</param>
/// <param name="Detail">Human-readable detail.</param>
public sealed record HandshakeReject(HandshakeRejectCode Code, string Detail) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.HandshakeReject;
}
