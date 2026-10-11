using Horyzont.Net.Transport;

namespace Horyzont.Client.Tests.Steam;

/// <summary>Transport double that records connects and sends and reports each connect as established on the next poll.</summary>
internal sealed class RecordingTransport : ITransport
{
    private readonly Queue<ConnectionId> _connected = new();
    private ulong _next = 1;

    public List<TransportEndpoint> Connects { get; } = [];

    public List<byte[]> Sent { get; } = [];

    public bool Disposed { get; private set; }

    public int MaxReliablePayload => 262144;

    public int MaxUnreliablePayload => 1150;

    public bool IsDrained => true;

    public void Listen(TransportEndpoint endpoint)
    {
    }

    public ConnectionId Connect(TransportEndpoint endpoint)
    {
        Connects.Add(endpoint);
        var id = new ConnectionId(_next++);
        _connected.Enqueue(id);
        return id;
    }

    public void Send(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());

    public void Disconnect(ConnectionId connection)
    {
    }

    public void Poll(ITransportEvents events)
    {
        while (_connected.TryDequeue(out var id))
        {
            events.OnConnected(id);
        }
    }

    public TransportStats GetStats(ConnectionId connection) => default;

    public TransportEndpoint GetRemoteEndpoint(ConnectionId connection) => Connects[(int)connection.Value - 1];

    public void Dispose() => Disposed = true;
}
