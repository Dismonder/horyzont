using System.Net;
using System.Net.Sockets;

using LiteNetLib;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Net.Transport.Udp;

/// <summary>Transport over UDP using LiteNetLib: one reliable ordered and one unreliable channel per connection.</summary>
public sealed partial class UdpTransport : ITransport
{
    /// <summary>Connection key every peer must present; foreign traffic is rejected.</summary>
    internal const string ConnectionKey = "horyzont/1";

    // LiteNetLib's default MTU of 1024 leaves 1023 bytes of unreliable payload, too little for 1150; 1164 gives 1163
    // and still fits a safe 1232 byte UDP payload.
    private const int Mtu = 1164;
    private const int UnreliableHeaderSize = 1;
    private const int ReliableLimit = 512 * 1024;

    private readonly NetManager _manager;
    private readonly TimeProvider _time;
    private readonly TimeSpan _closeTimeout;
    private readonly int _maxClosing;
    private readonly ILogger<UdpTransport> _logger;
    private readonly Dictionary<ulong, PeerLink> _connections = [];
    private readonly List<PeerLink> _closing = [];
    private readonly List<PeerLink> _pendingConnects = [];
    private readonly Queue<(ConnectionId Connection, TransportDisconnectReason Reason)> _localEvents = new();
    private ITransportEvents? _events;
    private ulong _nextConnectionId = 1;
    private bool _started;
    private bool _listening;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="UdpTransport"/> class.</summary>
    /// <param name="options">Timeout and keep-alive settings.</param>
    /// <param name="logger">Optional logger for network errors.</param>
    /// <param name="time">Clock of the closing deadline; the system clock when omitted.</param>
    public UdpTransport(TransportOptions options, ILogger<UdpTransport>? logger = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? NullLogger<UdpTransport>.Instance;
        _time = time ?? TimeProvider.System;
        _closeTimeout = options.CloseTimeout;
        _maxClosing = options.MaxClosingConnections;
        _manager = new NetManager(new Handler(this))
        {
            UnsyncedEvents = false,
            AutoRecycle = true,
            MtuOverride = Mtu,
            MtuDiscovery = false,
            EnableStatistics = true,
            IPv6Enabled = false,
            DisconnectTimeout = (int)options.Timeout.TotalMilliseconds,
            PingInterval = (int)options.KeepAlive.TotalMilliseconds,
        };
    }

    /// <inheritdoc />
    public int MaxReliablePayload => ReliableLimit;

    /// <inheritdoc />
    public int MaxUnreliablePayload => Mtu - UnreliableHeaderSize;

    /// <inheritdoc />
    /// <remarks>True when no connection is closing: each one waits for its reliable data to be acknowledged, at most <see cref="TransportOptions.CloseTimeout"/>.</remarks>
    public bool IsDrained => _closing.Count == 0;

    /// <summary>Gets the number of connections that are closing (tests).</summary>
    internal int ClosingCount => _closing.Count;

    /// <summary>Gets the UDP port this transport is bound to, or zero before it started.</summary>
    public int LocalPort => _started ? _manager.LocalPort : 0;

    /// <inheritdoc />
    public void Listen(TransportEndpoint endpoint)
    {
        if (endpoint is not UdpEndpoint udp)
        {
            throw new ArgumentException("Expected a UDP endpoint.", nameof(endpoint));
        }

        if (_started)
        {
            throw new InvalidOperationException("The transport is already running.");
        }

        if (!IPAddress.TryParse(udp.Host, out var address))
        {
            throw new ArgumentException($"'{udp.Host}' is not an IP address.", nameof(endpoint));
        }

        if (!_manager.Start(address, IPAddress.IPv6None, udp.Port))
        {
            throw new InvalidOperationException($"Could not bind UDP port {udp.Port}.");
        }

        _started = true;
        _listening = true;
    }

    /// <inheritdoc />
    public ConnectionId Connect(TransportEndpoint endpoint)
    {
        if (endpoint is not UdpEndpoint udp)
        {
            throw new ArgumentException("Expected a UDP endpoint.", nameof(endpoint));
        }

        if (!IPAddress.TryParse(udp.Host, out var address))
        {
            throw new ArgumentException($"'{udp.Host}' is not an IP address.", nameof(endpoint));
        }

        if (!_started)
        {
            if (!_manager.Start())
            {
                throw new InvalidOperationException("Could not open a UDP socket.");
            }

            _started = true;
        }

        var link = new PeerLink(new ConnectionId(_nextConnectionId++), new UdpEndpoint(udp.Host, udp.Port));
        var target = new IPEndPoint(address, udp.Port);
        _connections.Add(link.Id.Value, link);
        if (IsEndpointBusy(target))
        {
            // LiteNetLib keeps one peer per remote endpoint and would hand back the one still shutting down.
            link.Target = target;
            _pendingConnects.Add(link);
        }
        else
        {
            StartConnect(link, target);
        }

        return link.Id;
    }

    /// <inheritdoc />
    public void Send(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload)
    {
        var limit = channel == TransportChannel.ReliableOrdered ? MaxReliablePayload : MaxUnreliablePayload;
        if (payload.Length > limit)
        {
            throw new ArgumentException($"Payload of {payload.Length} bytes exceeds the {limit} byte limit of the channel.", nameof(payload));
        }

        if (_connections.TryGetValue(connection.Value, out var link) && link.IsOpen)
        {
            var method = channel == TransportChannel.ReliableOrdered ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;
            if (channel == TransportChannel.ReliableOrdered)
            {
                // LiteNetLib exposes no count of unacknowledged packets, so every reliable send asks for a delivery
                // event; Disconnect waits for them all (see FinishClosing).
                link.PendingReliable++;
                link.Peer!.SendWithDeliveryEvent(payload.ToArray(), method, link);
            }
            else
            {
                link.Peer!.Send(payload, 0, method);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>The disconnect is sent once every reliable message of the connection is acknowledged by the peer, or the connection is lost.</remarks>
    public void Disconnect(ConnectionId connection)
    {
        if (_connections.Remove(connection.Value, out var link))
        {
            _localEvents.Enqueue((connection, TransportDisconnectReason.Local));
            if (link.Peer is null)
            {
                // Still waiting for the previous connection to the same endpoint to finish closing.
                _pendingConnects.Remove(link);
                return;
            }

            link.Closing = true;
            link.ClosingSince = _time.GetTimestamp();
            _closing.Add(link);
            if (_closing.Count > _maxClosing)
            {
                // Bounded memory and sockets against peers that never acknowledge (E7): the oldest closing connection goes.
                ForceClose(_closing[0]);
                _closing.RemoveAt(0);
            }
        }
    }

    /// <inheritdoc />
    public void Poll(ITransportEvents events)
    {
        if (_disposed)
        {
            return;
        }

        _events = events;
        try
        {
            while (_localEvents.TryDequeue(out var local))
            {
                events.OnDisconnected(local.Connection, local.Reason);
            }

            if (_started)
            {
                _manager.PollEvents();
            }

            FinishClosing();
            StartPendingConnects();
        }
        finally
        {
            _events = null;
        }
    }

    /// <inheritdoc />
    public TransportStats GetStats(ConnectionId connection)
    {
        if (!_connections.TryGetValue(connection.Value, out var link) || link.Peer is null)
        {
            return default;
        }

        var statistics = link.Peer.Statistics;
        return new TransportStats(
            link.Peer.RoundTripTime,
            (float)statistics.PacketLossPercent / 100f,
            statistics.BytesSent,
            statistics.BytesReceived);
    }

    /// <inheritdoc />
    public TransportEndpoint GetRemoteEndpoint(ConnectionId connection) =>
        _connections.TryGetValue(connection.Value, out var link)
            ? link.Remote
            : throw new ArgumentException("Unknown connection.", nameof(connection));

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_started)
        {
            _manager.Stop(true);
        }

        _connections.Clear();
        _closing.Clear();
        _pendingConnects.Clear();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "UDP connection to {Remote} dropped while closing: {PendingReliable} reliable messages were not acknowledged")]
    private static partial void LogForcedClose(ILogger logger, string remote, int pendingReliable);

    private static TransportDisconnectReason Map(DisconnectReason reason) => reason switch
    {
        DisconnectReason.RemoteConnectionClose => TransportDisconnectReason.Remote,
        DisconnectReason.Timeout => TransportDisconnectReason.Timeout,
        DisconnectReason.ConnectionFailed => TransportDisconnectReason.Timeout,
        DisconnectReason.ConnectionRejected => TransportDisconnectReason.Refused,
        DisconnectReason.InvalidProtocol => TransportDisconnectReason.Refused,
        DisconnectReason.DisconnectPeerCalled => TransportDisconnectReason.Local,
        _ => TransportDisconnectReason.TransportError,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "UDP network error {Error} from {Address}")]
    private static partial void LogNetworkError(ILogger logger, SocketError error, string address);

    private void StartConnect(PeerLink link, IPEndPoint target)
    {
        link.Peer = _manager.Connect(target, ConnectionKey);
        link.Peer.Tag = link;
    }

    private bool IsEndpointBusy(IPEndPoint target)
    {
        foreach (var closing in _closing)
        {
            if (closing.Peer is { } peer && peer.Port == target.Port && peer.Address.Equals(target.Address))
            {
                return true;
            }
        }

        return false;
    }

    private void StartPendingConnects()
    {
        for (var i = _pendingConnects.Count - 1; i >= 0; i--)
        {
            var link = _pendingConnects[i];
            if (!IsEndpointBusy(link.Target!))
            {
                _pendingConnects.RemoveAt(i);
                StartConnect(link, link.Target!);
            }
        }
    }

    private void FinishClosing()
    {
        for (var i = _closing.Count - 1; i >= 0; i--)
        {
            var link = _closing[i];
            var peer = link.Peer!;
            if (!link.Released && _time.GetElapsedTime(link.ClosingSince) >= _closeTimeout)
            {
                ForceClose(link);
                _closing.RemoveAt(i);
                continue;
            }

            if (!link.DisconnectSent
                && (peer.ConnectionState != ConnectionState.Connected || link.PendingReliable <= 0))
            {
                link.DisconnectSent = true;
                peer.Disconnect();
            }

            // The peer keeps its endpoint slot in the manager until the shutdown completes.
            if (link.DisconnectSent && (link.Released || peer.ConnectionState == ConnectionState.Disconnected))
            {
                _closing.RemoveAt(i);
            }
        }
    }

    private void ForceClose(PeerLink link)
    {
        LogForcedClose(_logger, link.Remote.ToString() ?? string.Empty, link.PendingReliable);
        link.DisconnectSent = true;
        if (link.Peer is { ConnectionState: not ConnectionState.Disconnected } peer)
        {
            _manager.DisconnectPeerForce(peer);
        }
    }

    private sealed class PeerLink(ConnectionId id, TransportEndpoint remote)
    {
        public ConnectionId Id { get; } = id;

        public TransportEndpoint Remote { get; } = remote;

        public NetPeer? Peer { get; set; }

        public bool Closing { get; set; }

        /// <summary>Gets or sets the endpoint to connect to once the previous peer of that endpoint is gone.</summary>
        public IPEndPoint? Target { get; set; }

        public bool DisconnectSent { get; set; }

        /// <summary>Gets or sets the <see cref="TimeProvider"/> timestamp at which <see cref="Closing"/> started.</summary>
        public long ClosingSince { get; set; }

        public bool Released { get; set; }

        /// <summary>Gets or sets the number of reliable messages sent but not yet acknowledged by the peer.</summary>
        public int PendingReliable { get; set; }

        public bool IsOpen => !Closing && Peer is { ConnectionState: ConnectionState.Connected };
    }

    private sealed class Handler(UdpTransport owner) : INetEventListener
    {
        public void OnPeerConnected(NetPeer peer)
        {
            if (peer.Tag is not PeerLink link)
            {
                var remote = new UdpEndpoint(peer.Address.ToString(), peer.Port);
                link = new PeerLink(new ConnectionId(owner._nextConnectionId++), remote) { Peer = peer };
                peer.Tag = link;
                owner._connections.Add(link.Id.Value, link);
            }

            // A connection closed locally while still connecting was reported as disconnected already.
            if (!link.Closing)
            {
                owner._events?.OnConnected(link.Id);
            }
        }

        public void OnMessageDelivered(NetPeer peer, object userData)
        {
            if (userData is PeerLink link)
            {
                link.PendingReliable--;
            }
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            // Peers that never became connections (rejected requests) carry no link; closed-by-us links were reported already.
            if (peer.Tag is PeerLink link)
            {
                link.Released = true;
                if (!link.Closing && owner._connections.Remove(link.Id.Value))
                {
                    owner._events?.OnDisconnected(link.Id, Map(disconnectInfo.Reason));
                }
            }
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) =>
            LogNetworkError(owner._logger, socketError, endPoint.ToString());

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (peer.Tag is PeerLink { Closing: false } link)
            {
                var channel = deliveryMethod == DeliveryMethod.ReliableOrdered ? TransportChannel.ReliableOrdered : TransportChannel.Unreliable;
                owner._events?.OnReceived(link.Id, channel, reader.GetRemainingBytesSpan());
            }
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            if (owner._listening)
            {
                request.AcceptIfKey(ConnectionKey);
            }
            else
            {
                request.Reject();
            }
        }
    }
}
