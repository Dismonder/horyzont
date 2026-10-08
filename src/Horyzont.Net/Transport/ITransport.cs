namespace Horyzont.Net.Transport;

/// <summary>Moves bytes between peers over a reliable ordered and an unreliable channel.</summary>
public interface ITransport : IDisposable
{
    /// <summary>Gets the largest payload accepted on <see cref="TransportChannel.ReliableOrdered"/>; at least 256 KiB.</summary>
    int MaxReliablePayload { get; }

    /// <summary>Gets the largest payload accepted on <see cref="TransportChannel.Unreliable"/>; at least 1150 bytes.</summary>
    int MaxUnreliablePayload { get; }

    /// <summary>Starts accepting connections on <paramref name="endpoint"/>.</summary>
    /// <param name="endpoint">Endpoint of the transport's own kind.</param>
    void Listen(TransportEndpoint endpoint);

    /// <summary>Starts connecting to <paramref name="endpoint"/>; the outcome arrives via <see cref="ITransportEvents"/>.</summary>
    /// <param name="endpoint">Endpoint of the transport's own kind.</param>
    /// <returns>Identifier of the new connection.</returns>
    ConnectionId Connect(TransportEndpoint endpoint);

    /// <summary>Sends <paramref name="payload"/>; does nothing for a connection that is gone or not yet established.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="channel">Delivery channel.</param>
    /// <param name="payload">Message bytes; copied before the call returns.</param>
    /// <exception cref="ArgumentException">The payload exceeds the channel limit.</exception>
    void Send(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload);

    /// <summary>Closes the connection after queued reliable messages were sent, where the transport allows it.</summary>
    /// <param name="connection">The connection.</param>
    void Disconnect(ConnectionId connection);

    /// <summary>Delivers all pending events; call from the game thread only.</summary>
    /// <param name="events">Receiver of the events.</param>
    void Poll(ITransportEvents events);

    /// <summary>Returns statistics of a connection, or zeros for an unknown one.</summary>
    /// <param name="connection">The connection.</param>
    /// <returns>Current statistics.</returns>
    TransportStats GetStats(ConnectionId connection);

    /// <summary>Returns the identity of the other side; for Steam a <see cref="SteamEndpoint"/>.</summary>
    /// <param name="connection">The connection.</param>
    /// <returns>Remote endpoint.</returns>
    /// <exception cref="ArgumentException">The connection is unknown or already closed.</exception>
    TransportEndpoint GetRemoteEndpoint(ConnectionId connection);
}
