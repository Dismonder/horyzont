namespace Horyzont.Core.Protocol.Messages;

/// <summary>Connection ends.</summary>
/// <param name="Code">Reason.</param>
public sealed record Disconnect(DisconnectCode Code) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.Disconnect;
}
