using Horyzont.Net.Transport;

namespace Horyzont.Net.Tests.Transport;

/// <summary>Event sink that records everything a transport delivers, copying payloads.</summary>
public sealed class RecordingEvents : ITransportEvents
{
    public readonly record struct Message(ConnectionId Connection, TransportChannel Channel, byte[] Payload);

    public List<ConnectionId> Connected { get; } = [];

    public List<(ConnectionId Connection, TransportDisconnectReason Reason)> Disconnected { get; } = [];

    public List<Message> Received { get; } = [];

    public void OnConnected(ConnectionId connection) => Connected.Add(connection);

    public void OnDisconnected(ConnectionId connection, TransportDisconnectReason reason) =>
        Disconnected.Add((connection, reason));

    public void OnReceived(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload) =>
        Received.Add(new Message(connection, channel, payload.ToArray()));

    public IEnumerable<Message> ReceivedOn(TransportChannel channel) => Received.Where(m => m.Channel == channel);
}
