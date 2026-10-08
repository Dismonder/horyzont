namespace Horyzont.Core.Protocol;

/// <summary>Why the server refused a connection. Frozen together with the <see cref="Messages.HandshakeReject"/> format.</summary>
public enum HandshakeRejectCode : byte
{
    /// <summary>Different protocol version.</summary>
    WrongProtocol = 1,

    /// <summary>Different build.</summary>
    WrongBuild = 2,

    /// <summary>The server is full.</summary>
    ServerFull = 3,

    /// <summary>Wrong password.</summary>
    BadPassword = 4,

    /// <summary>The identity is banned.</summary>
    Banned = 5,

    /// <summary>The identity is already connected.</summary>
    AlreadyConnected = 6,

    /// <summary>The platform ticket does not match the identity.</summary>
    IdentityMismatch = 7,
}
