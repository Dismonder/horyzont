using Horyzont.Net.Transport.InMemory;
using Horyzont.Net.Transport.Steam;
using Horyzont.Net.Transport.Udp;

namespace Horyzont.Net.Transport;

/// <summary>
/// One transport over several others, e.g. a listen server that accepts the local player in memory and friends over
/// Steam or UDP. Connections of all inner transports get unique identifiers of this transport. Endpoints are routed by
/// kind: <see cref="InMemoryEndpoint"/> to <see cref="InMemoryTransport"/>, <see cref="UdpEndpoint"/> to
/// <see cref="UdpTransport"/>, <see cref="SteamEndpoint"/> to <see cref="SteamTransport"/> (the first inner transport of
/// that kind). Owns the inner transports and disposes them.
/// </summary>
public sealed class CompositeTransport : ITransport
{
    private readonly ITransport[] _inner;
    private readonly InnerEvents[] _adapters;
    private readonly Dictionary<(int Index, ulong Inner), ulong> _toOuter = [];
    private readonly Dictionary<ulong, (int Index, ConnectionId Inner)> _toInner = [];
    private ulong _nextId = 1;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="CompositeTransport"/> class.</summary>
    /// <param name="transports">Inner transports, at least one.</param>
    /// <exception cref="ArgumentException">The list is empty or contains null.</exception>
    public CompositeTransport(IReadOnlyList<ITransport> transports)
    {
        ArgumentNullException.ThrowIfNull(transports);
        if (transports.Count == 0)
        {
            throw new ArgumentException("At least one transport is required.", nameof(transports));
        }

        _inner = new ITransport[transports.Count];
        _adapters = new InnerEvents[transports.Count];
        for (var i = 0; i < transports.Count; i++)
        {
            _inner[i] = transports[i] ?? throw new ArgumentException("Transports must not be null.", nameof(transports));
            _adapters[i] = new InnerEvents(this, i);
        }
    }

    /// <inheritdoc />
    /// <remarks>True when every inner transport is drained.</remarks>
    public bool IsDrained
    {
        get
        {
            foreach (var transport in _inner)
            {
                if (!transport.IsDrained)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <inheritdoc />
    /// <remarks>The smallest limit of the inner transports.</remarks>
    public int MaxReliablePayload
    {
        get
        {
            var limit = int.MaxValue;
            foreach (var transport in _inner)
            {
                limit = Math.Min(limit, transport.MaxReliablePayload);
            }

            return limit;
        }
    }

    /// <inheritdoc />
    /// <remarks>The smallest limit of the inner transports.</remarks>
    public int MaxUnreliablePayload
    {
        get
        {
            var limit = int.MaxValue;
            foreach (var transport in _inner)
            {
                limit = Math.Min(limit, transport.MaxUnreliablePayload);
            }

            return limit;
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">No inner transport handles this kind of endpoint.</exception>
    public void Listen(TransportEndpoint endpoint) => _inner[Route(endpoint)].Listen(endpoint);

    /// <inheritdoc />
    /// <exception cref="ArgumentException">No inner transport handles this kind of endpoint.</exception>
    public ConnectionId Connect(TransportEndpoint endpoint)
    {
        var index = Route(endpoint);
        var inner = _inner[index].Connect(endpoint);
        return new ConnectionId(Map(index, inner));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The payload exceeds the limit this transport advertises for the channel.</exception>
    public void Send(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload)
    {
        // The advertised limit holds for every connection, whatever the limit of its inner transport (K1-07 review F5).
        if (payload.Length > (channel == TransportChannel.ReliableOrdered ? MaxReliablePayload : MaxUnreliablePayload))
        {
            throw new ArgumentException("The payload exceeds the channel limit.", nameof(payload));
        }

        if (_toInner.TryGetValue(connection.Value, out var target))
        {
            _inner[target.Index].Send(target.Inner, channel, payload);
        }
    }

    /// <inheritdoc />
    public void Disconnect(ConnectionId connection)
    {
        if (_toInner.TryGetValue(connection.Value, out var target))
        {
            _inner[target.Index].Disconnect(target.Inner);
        }
    }

    /// <inheritdoc />
    public void Poll(ITransportEvents events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (_disposed)
        {
            return;
        }

        for (var i = 0; i < _inner.Length; i++)
        {
            _adapters[i].Target = events;
            try
            {
                _inner[i].Poll(_adapters[i]);
            }
            finally
            {
                _adapters[i].Target = null;
            }
        }
    }

    /// <inheritdoc />
    public TransportStats GetStats(ConnectionId connection) =>
        _toInner.TryGetValue(connection.Value, out var target) ? _inner[target.Index].GetStats(target.Inner) : default;

    /// <inheritdoc />
    public TransportEndpoint GetRemoteEndpoint(ConnectionId connection) =>
        _toInner.TryGetValue(connection.Value, out var target)
            ? _inner[target.Index].GetRemoteEndpoint(target.Inner)
            : throw new ArgumentException("Unknown connection.", nameof(connection));

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var transport in _inner)
        {
            transport.Dispose();
        }

        _toInner.Clear();
        _toOuter.Clear();
    }

    private static bool Handles(ITransport transport, TransportEndpoint endpoint) => (transport, endpoint) switch
    {
        (InMemoryTransport, InMemoryEndpoint) => true,
        (UdpTransport, UdpEndpoint) => true,
        (SteamTransport, SteamEndpoint) => true,
        _ => false,
    };

    private int Route(TransportEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        for (var i = 0; i < _inner.Length; i++)
        {
            if (Handles(_inner[i], endpoint))
            {
                return i;
            }
        }

        throw new ArgumentException($"No inner transport handles a {endpoint.GetType().Name}.", nameof(endpoint));
    }

    private ulong Map(int index, ConnectionId inner)
    {
        if (!_toOuter.TryGetValue((index, inner.Value), out var outer))
        {
            outer = _nextId++;
            _toOuter.Add((index, inner.Value), outer);
            _toInner.Add(outer, (index, inner));
        }

        return outer;
    }

    private void Unmap(int index, ConnectionId inner, ulong outer)
    {
        _toOuter.Remove((index, inner.Value));
        _toInner.Remove(outer);
    }

    private sealed class InnerEvents(CompositeTransport owner, int index) : ITransportEvents
    {
        public ITransportEvents? Target { get; set; }

        public void OnConnected(ConnectionId connection) => Target!.OnConnected(new ConnectionId(owner.Map(index, connection)));

        public void OnDisconnected(ConnectionId connection, TransportDisconnectReason reason)
        {
            var outer = owner.Map(index, connection);
            owner.Unmap(index, connection, outer);
            Target!.OnDisconnected(new ConnectionId(outer), reason);
        }

        public void OnReceived(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload) =>
            Target!.OnReceived(new ConnectionId(owner.Map(index, connection)), channel, payload);
    }
}
