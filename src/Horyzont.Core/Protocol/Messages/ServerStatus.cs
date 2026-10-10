namespace Horyzont.Core.Protocol.Messages;

/// <summary>Server availability information; unknown reasons remain readable.</summary>
/// <param name="ReadOnly">Whether changes are suspended.</param>
/// <param name="Reason">0 running, 1 terrain mismatch, 2 writer fault.</param>
/// <param name="Detail">Technical plain text, at most 256 UTF-8 bytes.</param>
public sealed record ServerStatus(bool ReadOnly, byte Reason, string Detail) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ServerStatus;
}
