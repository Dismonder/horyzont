namespace Horyzont.Net.Transport.InMemory;

/// <summary>One packet in flight; <see cref="Payload"/> is rented from the shared array pool (null when empty).</summary>
internal readonly record struct InMemoryPacket(
    InMemoryPacketKind Kind,
    InMemoryTransport Source,
    ulong SourceConnection,
    ulong DestinationConnection,
    TransportChannel Channel,
    byte[]? Payload,
    int Length);
