using Horyzont.Net.Transport;

namespace Horyzont.Client.Platform;

/// <summary>Records successful disposal, including when a server disposes its transport on a worker thread.</summary>
/// <remarks>Forwarding preserves transport semantics. IsReleased is read on the main thread after shutdown.</remarks>
public sealed class ReleaseTrackedTransport : ITransport
{
    private readonly ITransport _inner;
    private int _released;

    /// <summary>Initializes an owning transport wrapper.</summary>
    /// <param name="inner">Transport disposed by this wrapper.</param>
    public ReleaseTrackedTransport(ITransport inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Gets a value indicating whether Dispose returned successfully.</summary>
    public bool IsReleased => Volatile.Read(ref _released) != 0;

    /// <inheritdoc />
    public int MaxReliablePayload => _inner.MaxReliablePayload;

    /// <inheritdoc />
    public int MaxUnreliablePayload => _inner.MaxUnreliablePayload;

    /// <inheritdoc />
    public bool IsDrained => _inner.IsDrained;

    /// <inheritdoc />
    public void Listen(TransportEndpoint endpoint) => _inner.Listen(endpoint);

    /// <inheritdoc />
    public ConnectionId Connect(TransportEndpoint endpoint) => _inner.Connect(endpoint);

    /// <inheritdoc />
    public void Send(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload) => _inner.Send(connection, channel, payload);

    /// <inheritdoc />
    public void Disconnect(ConnectionId connection) => _inner.Disconnect(connection);

    /// <inheritdoc />
    public void Poll(ITransportEvents events) => _inner.Poll(events);

    /// <inheritdoc />
    public TransportStats GetStats(ConnectionId connection) => _inner.GetStats(connection);

    /// <inheritdoc />
    public TransportEndpoint GetRemoteEndpoint(ConnectionId connection) => _inner.GetRemoteEndpoint(connection);

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsReleased)
        {
            return;
        }

        _inner.Dispose();
        Volatile.Write(ref _released, 1);
    }
}
