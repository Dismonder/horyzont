using Horyzont.Net.Transport;

namespace Horyzont.Server.Tests.Support;

/// <summary>A transport whose <c>Dispose</c> blocks until the gate opens (LiteNetLib <c>Stop(true)</c> joins its thread without a limit).</summary>
internal sealed class GatedTransport(ITransport inner) : ITransport
{
    /// <summary>Gets the gate; <c>Dispose</c> waits for it.</summary>
    public ManualResetEventSlim Gate { get; } = new();

    /// <summary>Gets a value indicating whether <c>Dispose</c> was entered.</summary>
    public volatile bool DisposeEntered;

    /// <summary>Gets the managed thread that entered <c>Dispose</c>.</summary>
    public int DisposeThreadId { get; private set; }

    public int MaxReliablePayload => inner.MaxReliablePayload;

    public int MaxUnreliablePayload => inner.MaxUnreliablePayload;

    public bool IsDrained => inner.IsDrained;

    public void Listen(TransportEndpoint endpoint) => inner.Listen(endpoint);

    public ConnectionId Connect(TransportEndpoint endpoint) => inner.Connect(endpoint);

    public void Send(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload) => inner.Send(connection, channel, payload);

    public void Disconnect(ConnectionId connection) => inner.Disconnect(connection);

    public void Poll(ITransportEvents events) => inner.Poll(events);

    public TransportStats GetStats(ConnectionId connection) => inner.GetStats(connection);

    public TransportEndpoint GetRemoteEndpoint(ConnectionId connection) => inner.GetRemoteEndpoint(connection);

    public void Dispose()
    {
        DisposeThreadId = Environment.CurrentManagedThreadId;
        DisposeEntered = true;
        Gate.Wait();
        inner.Dispose();
    }
}
