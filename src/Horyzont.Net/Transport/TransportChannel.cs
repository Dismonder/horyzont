namespace Horyzont.Net.Transport;

/// <summary>Delivery guarantee of a transport message.</summary>
public enum TransportChannel : byte
{
    /// <summary>Every message arrives exactly once and in send order.</summary>
    ReliableOrdered = 0,

    /// <summary>Messages may be lost, duplicated or reordered.</summary>
    Unreliable = 1,
}
