namespace Horyzont.Core.Protocol.Messages;

/// <summary>Client loaded the world and is ready to play.</summary>
public sealed record ClientReady : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ClientReady;
}
