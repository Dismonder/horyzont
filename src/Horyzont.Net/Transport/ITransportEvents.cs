namespace Horyzont.Net.Transport;

/// <summary>Receiver of transport events, delivered only from <see cref="ITransport.Poll"/>.</summary>
public interface ITransportEvents
{
    /// <summary>A connection was established.</summary>
    /// <param name="connection">The connection.</param>
    void OnConnected(ConnectionId connection);

    /// <summary>A connection ended or a connection attempt failed.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="reason">Why it ended.</param>
    void OnDisconnected(ConnectionId connection, TransportDisconnectReason reason);

    /// <summary>A message arrived.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="channel">The channel it arrived on.</param>
    /// <param name="payload">Message bytes, valid only for the duration of the call.</param>
    void OnReceived(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload);
}
