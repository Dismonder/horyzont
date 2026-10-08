namespace Horyzont.Net.Session;

/// <summary>State of a <see cref="ClientSession"/>.</summary>
public enum ClientState : byte
{
    /// <summary>No connection.</summary>
    Disconnected = 0,

    /// <summary>The transport connects.</summary>
    Connecting = 1,

    /// <summary>ClientHello was sent.</summary>
    AwaitingServerHello = 2,

    /// <summary>ServerHello arrived; the game loads the world.</summary>
    Loading = 3,

    /// <summary>ClientReady was sent; input and intents are allowed.</summary>
    InGame = 4,

    /// <summary>The session ended; waiting for the transport to close.</summary>
    Closing = 5,
}
