namespace Horyzont.Core.Protocol;

/// <summary>Why a connection ended.</summary>
public enum DisconnectCode : byte
{
    /// <summary>Normal close.</summary>
    Normal = 0,

    /// <summary>No traffic for too long.</summary>
    Timeout = 1,

    /// <summary>The peer broke the protocol.</summary>
    ProtocolViolation = 2,

    /// <summary>Kicked by an administrator.</summary>
    Kicked = 3,

    /// <summary>The server stops.</summary>
    ServerShutdown = 4,

    /// <summary>The host of a listen server left.</summary>
    HostLeft = 5,

    /// <summary>The handshake took too long.</summary>
    HandshakeTimeout = 6,
}
