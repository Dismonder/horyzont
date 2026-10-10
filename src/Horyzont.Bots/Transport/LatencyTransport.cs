using Horyzont.Net.Transport;

namespace Horyzont.Bots.Transport;

/// <summary>
/// Decorator of a client transport that delays everything by a fixed time plus a jitter in both directions (a stand-in for a real
/// link: the runner puts it on a loopback UDP socket to get the round trip of 100 ms). Time comes from the injected
/// <see cref="TimeProvider"/>. Messages of the reliable channel and connection events keep their order; unreliable messages may
/// overtake each other within the jitter. It never loses a packet. Main thread only.
/// </summary>
public sealed class LatencyTransport : ITransport
{
    private readonly ITransport _inner;
    private readonly TimeProvider _time;
    private readonly long _delay;
    private readonly long _jitter;
    private readonly Random _random;
    private readonly PriorityQueue<Outgoing, (long At, long Order)> _outgoing = new();
    private readonly PriorityQueue<Incoming, (long At, long Order)> _incoming = new();
    private readonly Capture _capture;
    private long _order;
    private long _lastReliableOut;
    private long _lastReliableIn;

    /// <summary>Initializes a new instance of the <see cref="LatencyTransport"/> class.</summary>
    /// <param name="inner">The transport to wrap; disposed with this one.</param>
    /// <param name="time">The clock.</param>
    /// <param name="oneWayDelay">Delay of each direction.</param>
    /// <param name="jitter">Largest deviation (plus or minus) of the delay of one message.</param>
    /// <param name="seed">Seed of the jitter.</param>
    public LatencyTransport(ITransport inner, TimeProvider time, TimeSpan oneWayDelay, TimeSpan jitter, int seed)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThan(oneWayDelay, jitter);
        ArgumentOutOfRangeException.ThrowIfNegative(jitter.Ticks);
        _inner = inner;
        _time = time;
        _delay = ToTimestamp(oneWayDelay);
        _jitter = ToTimestamp(jitter);
        _random = new Random(seed);
        _capture = new Capture(this);
    }

    /// <inheritdoc />
    public int MaxReliablePayload => _inner.MaxReliablePayload;

    /// <inheritdoc />
    public int MaxUnreliablePayload => _inner.MaxUnreliablePayload;

    /// <inheritdoc />
    public bool IsDrained => _inner.IsDrained && _outgoing.Count == 0 && _incoming.Count == 0;

    /// <inheritdoc />
    public void Listen(TransportEndpoint endpoint) => _inner.Listen(endpoint);

    /// <inheritdoc />
    public ConnectionId Connect(TransportEndpoint endpoint) => _inner.Connect(endpoint);

    /// <inheritdoc />
    public void Send(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload)
    {
        var at = Schedule(channel == TransportChannel.ReliableOrdered, ref _lastReliableOut);
        _outgoing.Enqueue(new Outgoing(connection, channel, payload.ToArray()), (at, _order++));
    }

    /// <inheritdoc />
    public void Disconnect(ConnectionId connection) => _inner.Disconnect(connection);

    /// <inheritdoc />
    public void Poll(ITransportEvents events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var now = _time.GetTimestamp();
        while (_outgoing.TryPeek(out var next, out var key) && key.At <= now)
        {
            _outgoing.Dequeue();
            _inner.Send(next.Connection, next.Channel, next.Payload);
        }

        _inner.Poll(_capture);
        while (_incoming.TryPeek(out var next, out var key) && key.At <= now)
        {
            _incoming.Dequeue();
            switch (next.Kind)
            {
                case IncomingKind.Connected:
                    events.OnConnected(next.Connection);
                    break;
                case IncomingKind.Disconnected:
                    events.OnDisconnected(next.Connection, next.Reason);
                    break;
                default:
                    events.OnReceived(next.Connection, next.Channel, next.Payload);
                    break;
            }
        }
    }

    /// <inheritdoc />
    public TransportStats GetStats(ConnectionId connection) => _inner.GetStats(connection);

    /// <inheritdoc />
    public TransportEndpoint GetRemoteEndpoint(ConnectionId connection) => _inner.GetRemoteEndpoint(connection);

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();

    private long ToTimestamp(TimeSpan span) => (long)(span.TotalSeconds * _time.TimestampFrequency);

    /// <summary>Delivery time of a new message; an ordered one never overtakes the previous ordered one.</summary>
    private long Schedule(bool ordered, ref long lastOrdered)
    {
        var jitter = _jitter == 0 ? 0 : _random.NextInt64(-_jitter, _jitter + 1);
        var at = _time.GetTimestamp() + _delay + jitter;
        if (!ordered)
        {
            return at;
        }

        at = Math.Max(at, lastOrdered);
        lastOrdered = at;
        return at;
    }

    private enum IncomingKind
    {
        Connected,
        Disconnected,
        Data,
    }

    private readonly record struct Outgoing(ConnectionId Connection, TransportChannel Channel, byte[] Payload);

    private readonly record struct Incoming(
        IncomingKind Kind, ConnectionId Connection, TransportChannel Channel, byte[] Payload, TransportDisconnectReason Reason);

    private sealed class Capture(LatencyTransport owner) : ITransportEvents
    {
        public void OnConnected(ConnectionId connection) =>
            owner._incoming.Enqueue(
                new Incoming(IncomingKind.Connected, connection, default, [], default),
                (owner.Schedule(true, ref owner._lastReliableIn), owner._order++));

        public void OnDisconnected(ConnectionId connection, TransportDisconnectReason reason) =>
            owner._incoming.Enqueue(
                new Incoming(IncomingKind.Disconnected, connection, default, [], reason),
                (owner.Schedule(true, ref owner._lastReliableIn), owner._order++));

        public void OnReceived(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload) =>
            owner._incoming.Enqueue(
                new Incoming(IncomingKind.Data, connection, channel, payload.ToArray(), default),
                (owner.Schedule(channel == TransportChannel.ReliableOrdered, ref owner._lastReliableIn), owner._order++));
    }
}
