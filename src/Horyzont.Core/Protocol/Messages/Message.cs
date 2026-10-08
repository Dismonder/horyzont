namespace Horyzont.Core.Protocol.Messages;

/// <summary>A protocol message. Arrays and lists belong to the record; decoders always copy them.</summary>
public abstract record Message
{
    private protected Message()
    {
    }

    /// <summary>Message code.</summary>
    public abstract MessageType Type { get; }
}
