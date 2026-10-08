namespace Horyzont.Net.Transport;

/// <summary>Why a connection ended or could not be established.</summary>
public enum TransportDisconnectReason : byte
{
    /// <summary>This side called <see cref="ITransport.Disconnect"/>.</summary>
    Local = 0,

    /// <summary>The other side closed the connection.</summary>
    Remote = 1,

    /// <summary>No traffic from the other side for the configured timeout.</summary>
    Timeout = 2,

    /// <summary>The other side was not listening or rejected the connection attempt.</summary>
    Refused = 3,

    /// <summary>The underlying library or socket reported an error.</summary>
    TransportError = 4,
}
