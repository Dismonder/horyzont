namespace Horyzont.Net.Session;

/// <summary>State of one client connection on the server; it decides which messages the client may send.</summary>
public enum PeerState : byte
{
    /// <summary>Connected, waiting for ClientHello.</summary>
    AwaitingHello = 0,

    /// <summary>Accepted; the client loads the world.</summary>
    Loading = 1,

    /// <summary>The client sent ClientReady and plays.</summary>
    InGame = 2,

    /// <summary>The final message was sent; the transport connection closes after the close delay.</summary>
    Closing = 3,
}
