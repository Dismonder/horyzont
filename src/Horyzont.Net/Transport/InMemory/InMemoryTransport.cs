using System.Buffers;

namespace Horyzont.Net.Transport.InMemory;

/// <summary>
/// Transport that moves bytes between instances of one <see cref="InMemoryNetwork"/> with simulated latency,
/// jitter, loss and duplication. All timing comes from the network's <see cref="TimeProvider"/>.
/// </summary>
public sealed class InMemoryTransport : ITransport
{
    private const int ReliableLimit = 512 * 1024;
    private const int UnreliableLimit = 1200;

    private readonly InMemoryNetwork _network;
    private readonly string _name;
    private readonly TransportOptions _options;
    private readonly ulong? _fakeSteamId;
    private readonly Dictionary<ulong, Connection> _connections = [];
    private readonly List<Connection> _connectionList = [];
    private readonly PriorityQueue<InMemoryPacket, (long Due, long Sequence)> _inbox = new();
    private readonly Queue<(ConnectionId Connection, TransportDisconnectReason Reason)> _localEvents = new();
    private ulong _nextConnectionId = 1;
    private long _sequence;
    private int _inFlight;
    private bool _listening;
    private bool _disposed;

    internal InMemoryTransport(InMemoryNetwork network, string name, TransportOptions options, ulong? fakeSteamId)
    {
        _network = network;
        _name = name;
        _options = options;
        _fakeSteamId = fakeSteamId;
    }

    /// <inheritdoc />
    public int MaxReliablePayload => ReliableLimit;

    /// <inheritdoc />
    public int MaxUnreliablePayload => UnreliableLimit;

    /// <inheritdoc />
    /// <remarks>True when no packet of this transport (other than keep-alives) waits in the inbox of another one.</remarks>
    public bool IsDrained => _inFlight == 0;

    private TransportEndpoint Identity => _fakeSteamId is { } id ? new SteamEndpoint(id) : new InMemoryEndpoint(_name);

    /// <inheritdoc />
    public void Listen(TransportEndpoint endpoint)
    {
        if (endpoint is not InMemoryEndpoint memory || memory.Name != _name)
        {
            throw new ArgumentException("An in-memory transport can only listen on its own name.", nameof(endpoint));
        }

        _listening = true;
    }

    /// <inheritdoc />
    public ConnectionId Connect(TransportEndpoint endpoint)
    {
        if (endpoint is not InMemoryEndpoint memory)
        {
            throw new ArgumentException("Expected an in-memory endpoint.", nameof(endpoint));
        }

        var id = _nextConnectionId++;
        if (_disposed || memory.Name == _name || !_network.TryGetTransport(memory.Name, out var target))
        {
            _localEvents.Enqueue((new ConnectionId(id), TransportDisconnectReason.Refused));
            return new ConnectionId(id);
        }

        var connection = new Connection(id, target, target.Identity, _network.GetLink(_name, memory.Name), Now());
        Add(connection);
        SendPacket(connection, InMemoryPacketKind.ConnectRequest, TransportChannel.ReliableOrdered, default);
        return new ConnectionId(id);
    }

    /// <inheritdoc />
    public void Send(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload)
    {
        var limit = channel == TransportChannel.ReliableOrdered ? ReliableLimit : UnreliableLimit;
        if (payload.Length > limit)
        {
            throw new ArgumentException($"Payload of {payload.Length} bytes exceeds the {limit} byte limit of the channel.", nameof(payload));
        }

        if (_connections.TryGetValue(connection.Value, out var c) && c.State == ConnectionState.Connected)
        {
            SendPacket(c, InMemoryPacketKind.Data, channel, payload);
        }
    }

    /// <inheritdoc />
    public void Disconnect(ConnectionId connection)
    {
        if (!_connections.TryGetValue(connection.Value, out var c))
        {
            return;
        }

        SendPacket(c, InMemoryPacketKind.Disconnect, TransportChannel.ReliableOrdered, default);
        Remove(c);
        _localEvents.Enqueue((connection, TransportDisconnectReason.Local));
    }

    /// <inheritdoc />
    public void Poll(ITransportEvents events)
    {
        if (_disposed)
        {
            return;
        }

        var now = Now();
        while (_localEvents.TryDequeue(out var local))
        {
            events.OnDisconnected(local.Connection, local.Reason);
        }

        while (_inbox.TryPeek(out var packet, out var key) && key.Due <= now)
        {
            _inbox.Dequeue();
            packet.Source.Delivered(packet);
            Handle(packet, now, events);
            if (packet.Payload is not null)
            {
                ArrayPool<byte>.Shared.Return(packet.Payload);
            }
        }

        var timeout = _options.Timeout.Ticks;
        var keepAlive = _options.KeepAlive.Ticks;
        for (var i = _connectionList.Count - 1; i >= 0; i--)
        {
            if (i >= _connectionList.Count)
            {
                continue;
            }

            var c = _connectionList[i];
            if (c.Held is { Count: > 0 } && !c.Link.Partitioned)
            {
                FlushHeld(c, now);
            }

            if (now - c.LastReceived >= timeout)
            {
                Remove(c);
                events.OnDisconnected(new ConnectionId(c.Id), TransportDisconnectReason.Timeout);
            }
            else if (c.State == ConnectionState.Connected && now - c.LastSent >= keepAlive)
            {
                SendPacket(c, InMemoryPacketKind.KeepAlive, TransportChannel.ReliableOrdered, default);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>RTT is derived from the configured link conditions; loss is the observed share of dropped unreliable messages.</remarks>
    public TransportStats GetStats(ConnectionId connection)
    {
        if (!_connections.TryGetValue(connection.Value, out var c))
        {
            return default;
        }

        var conditions = c.Link.Conditions;
        var rtt = 2 * (conditions.Latency + (conditions.Jitter / 2)).TotalMilliseconds;
        var loss = c.UnreliableSent == 0 ? 0f : (float)c.UnreliableDropped / c.UnreliableSent;
        return new TransportStats((float)rtt, loss, c.BytesSent, c.BytesReceived);
    }

    /// <inheritdoc />
    public TransportEndpoint GetRemoteEndpoint(ConnectionId connection) =>
        _connections.TryGetValue(connection.Value, out var c)
            ? c.RemoteEndpoint
            : throw new ArgumentException("Unknown connection.", nameof(connection));

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _network.Remove(_name);
        _connections.Clear();
        _connectionList.Clear();
        while (_inbox.TryDequeue(out var packet, out _))
        {
            packet.Source.Delivered(packet);
            if (packet.Payload is not null)
            {
                ArrayPool<byte>.Shared.Return(packet.Payload);
            }
        }
    }

    private long Now() => _network.Time.GetUtcNow().UtcTicks;

    private void Add(Connection connection)
    {
        _connections.Add(connection.Id, connection);
        _connectionList.Add(connection);
    }

    private void Remove(Connection connection)
    {
        connection.State = ConnectionState.Closed;
        if (connection.Held is not null)
        {
            foreach (var held in connection.Held)
            {
                if (held.Payload is not null)
                {
                    ArrayPool<byte>.Shared.Return(held.Payload);
                }
            }

            connection.Held.Clear();
        }

        _connections.Remove(connection.Id);
        _connectionList.Remove(connection);
    }

    private void Handle(InMemoryPacket packet, long now, ITransportEvents events)
    {
        if (packet.Kind == InMemoryPacketKind.ConnectRequest)
        {
            HandleConnectRequest(packet, now, events);
            return;
        }

        if (!_connections.TryGetValue(packet.DestinationConnection, out var c))
        {
            if (packet.Kind == InMemoryPacketKind.Disconnect && packet.DestinationConnection == 0)
            {
                CloseByRemote(packet, events);
            }

            return;
        }

        c.LastReceived = now;
        switch (packet.Kind)
        {
            case InMemoryPacketKind.ConnectAccept:
                if (c.State == ConnectionState.Connecting)
                {
                    c.State = ConnectionState.Connected;
                    c.RemoteConnection = packet.SourceConnection;
                    events.OnConnected(new ConnectionId(c.Id));
                }

                break;
            case InMemoryPacketKind.ConnectRefuse:
                Remove(c);
                events.OnDisconnected(new ConnectionId(c.Id), TransportDisconnectReason.Refused);
                break;
            case InMemoryPacketKind.Data:
                if (c.State == ConnectionState.Connected)
                {
                    c.BytesReceived += packet.Length;
                    events.OnReceived(new ConnectionId(c.Id), packet.Channel, packet.Payload.AsSpan(0, packet.Length));
                }

                break;
            case InMemoryPacketKind.Disconnect:
                Remove(c);
                events.OnDisconnected(new ConnectionId(c.Id), TransportDisconnectReason.Remote);
                break;
            default:
                break;
        }
    }

    private void HandleConnectRequest(InMemoryPacket packet, long now, ITransportEvents events)
    {
        var source = packet.Source;
        if (!_listening)
        {
            var link = _network.GetLink(_name, source._name);
            var refusal = new InMemoryPacket(InMemoryPacketKind.ConnectRefuse, this, 0, packet.SourceConnection, TransportChannel.ReliableOrdered, null, 0);
            var lastOrdered = 0L;
            source.Enqueue(link, refusal, ref lastOrdered, now);
            return;
        }

        var connection = new Connection(_nextConnectionId++, source, source.Identity, _network.GetLink(_name, source._name), now)
        {
            State = ConnectionState.Connected,
            RemoteConnection = packet.SourceConnection,
        };
        Add(connection);
        SendPacket(connection, InMemoryPacketKind.ConnectAccept, TransportChannel.ReliableOrdered, default);
        events.OnConnected(new ConnectionId(connection.Id));
    }

    private void CloseByRemote(InMemoryPacket packet, ITransportEvents events)
    {
        // A client that disconnected before the accept arrived addresses us only by its own connection id.
        for (var i = 0; i < _connectionList.Count; i++)
        {
            var c = _connectionList[i];
            if (c.RemoteConnection == packet.SourceConnection && ReferenceEquals(c.Remote, packet.Source))
            {
                Remove(c);
                events.OnDisconnected(new ConnectionId(c.Id), TransportDisconnectReason.Remote);
                return;
            }
        }
    }

    private void SendPacket(Connection c, InMemoryPacketKind kind, TransportChannel channel, ReadOnlySpan<byte> payload)
    {
        var now = Now();
        c.LastSent = now;
        c.BytesSent += payload.Length;
        if (kind == InMemoryPacketKind.Data && channel == TransportChannel.Unreliable)
        {
            c.UnreliableSent++;
        }

        byte[]? buffer = null;
        if (!payload.IsEmpty)
        {
            buffer = ArrayPool<byte>.Shared.Rent(payload.Length);
            payload.CopyTo(buffer);
        }

        var packet = new InMemoryPacket(kind, this, c.Id, c.RemoteConnection, channel, buffer, payload.Length);
        if (c.Link.Partitioned)
        {
            // A real reliable stream retransmits across a short outage, so ordered data is retained and delivered in
            // order after Heal; everything else (keep-alives, unreliable data, disconnects) is lost with the link.
            if (kind is InMemoryPacketKind.Data or InMemoryPacketKind.ConnectRequest or InMemoryPacketKind.ConnectAccept
                && channel == TransportChannel.ReliableOrdered)
            {
                (c.Held ??= []).Add(packet);
            }
            else
            {
                Discard(c, packet);
            }

            return;
        }

        if (c.Held is { Count: > 0 })
        {
            FlushHeld(c, now);
        }

        if (kind == InMemoryPacketKind.Data && channel == TransportChannel.Unreliable)
        {
            if (c.Link.NextDouble() < c.Link.Conditions.LossRate)
            {
                Discard(c, packet);
                return;
            }

            if (c.Link.NextDouble() < c.Link.Conditions.DuplicateRate)
            {
                var copy = packet;
                if (buffer is not null)
                {
                    copy = packet with { Payload = ArrayPool<byte>.Shared.Rent(payload.Length) };
                    payload.CopyTo(copy.Payload);
                }

                var unordered = 0L;
                c.Remote.Enqueue(c.Link, copy, ref unordered, now);
            }
        }

        c.Remote.Enqueue(c.Link, packet, ref c.LastOrderedDue, now);
    }

    private static void FlushHeld(Connection c, long now)
    {
        foreach (var held in c.Held!)
        {
            c.Remote.Enqueue(c.Link, held, ref c.LastOrderedDue, now);
        }

        c.Held!.Clear();
    }

    private static void Discard(Connection c, InMemoryPacket packet)
    {
        if (packet.Kind == InMemoryPacketKind.Data && packet.Channel == TransportChannel.Unreliable)
        {
            c.UnreliableDropped++;
        }

        if (packet.Payload is not null)
        {
            ArrayPool<byte>.Shared.Return(packet.Payload);
        }
    }

    /// <summary>Queues <paramref name="packet"/> for delivery here; ordered traffic never overtakes earlier ordered traffic.</summary>
    private void Enqueue(InMemoryLink link, InMemoryPacket packet, ref long lastOrderedDue, long now)
    {
        if (_disposed)
        {
            if (packet.Payload is not null)
            {
                ArrayPool<byte>.Shared.Return(packet.Payload);
            }

            return;
        }

        var due = now + link.NextDelay().Ticks;
        var ordered = packet.Kind != InMemoryPacketKind.Data || packet.Channel == TransportChannel.ReliableOrdered;
        if (ordered)
        {
            due = Math.Max(due, lastOrderedDue);
            lastOrderedDue = due;
        }

        if (packet.Kind != InMemoryPacketKind.KeepAlive)
        {
            packet.Source._inFlight++;
        }

        _inbox.Enqueue(packet, (due, _sequence++));
    }

    private void Delivered(InMemoryPacket packet)
    {
        if (packet.Kind != InMemoryPacketKind.KeepAlive)
        {
            _inFlight--;
        }
    }

    private enum ConnectionState : byte
    {
        Connecting,
        Connected,
        Closed,
    }

    private sealed class Connection(ulong id, InMemoryTransport remote, TransportEndpoint remoteEndpoint, InMemoryLink link, long now)
    {
        public ulong Id { get; } = id;

        public InMemoryTransport Remote { get; } = remote;

        public TransportEndpoint RemoteEndpoint { get; } = remoteEndpoint;

        public InMemoryLink Link { get; } = link;

        public ConnectionState State { get; set; } = ConnectionState.Connecting;

        public ulong RemoteConnection { get; set; }

        public long LastReceived { get; set; } = now;

        public long LastSent { get; set; } = now;

        public long BytesSent { get; set; }

        public long BytesReceived { get; set; }

        public long UnreliableSent { get; set; }

        public long UnreliableDropped { get; set; }

        public List<InMemoryPacket>? Held { get; set; }

        public long LastOrderedDue;
    }
}
