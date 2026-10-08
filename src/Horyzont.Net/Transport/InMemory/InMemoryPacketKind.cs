namespace Horyzont.Net.Transport.InMemory;

/// <summary>Kind of a packet travelling between in-memory transports.</summary>
internal enum InMemoryPacketKind : byte
{
    ConnectRequest,
    ConnectAccept,
    ConnectRefuse,
    Data,
    KeepAlive,
    Disconnect,
}
