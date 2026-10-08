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
    private readonly ILogger<UdpTransport> _logger;
    private readonly Dictionary<ulong, PeerLink> _connections = [];
    private readonly List<PeerLink> _closing = [];
    private readonly Queue<(ConnectionId Connection, TransportDisconnectReason Reason)> _localEvents = new();
    private ITransportEvents? _events;
    private ulong _nextConnectionId = 1;
    private bool _started;
    private bool _listening;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="UdpTransport"/> class.</summary>
    /// <param name="options">Timeout and keep-alive settings.</param>
    /// <param name="logger">Optional logger for network errors.</param>
    public UdpTransport(TransportOptions options, ILogger<UdpTransport>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? NullLogger<UdpTransport>.Instance;
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
        link.Peer = _manager.Connect(new IPEndPoint(address, udp.Port), ConnectionKey);
        link.Peer.Tag = link;
        _connections.Add(link.Id.Value, link);
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
            link.Peer!.Send(payload, 0, method);
        }
    }

    /// <inheritdoc />
    /// <remarks>The disconnect is sent once the reliable queue of the connection is empty.</remarks>
    public void Disconnect(ConnectionId connection)
    {
        if (_connections.Remove(connection.Value, out var link))
        {
            link.Closing = true;
            _closing.Add(link);
            _localEvents.Enqueue((connection, TransportDisconnectReason.Local));
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
    }

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

    private void FinishClosing()
    {
        for (var i = _closing.Count - 1; i >= 0; i--)
        {
            var link = _closing[i];
            var peer = link.Peer;
            if (peer is null || peer.ConnectionState != ConnectionState.Connected)
            {
                _closing.RemoveAt(i);
                peer?.Disconnect();
            }
            else if (peer.GetPacketsCountInReliableQueue(0, true) == 0)
            {
                _closing.RemoveAt(i);
                peer.Disconnect();
            }
        }
    }

    private sealed class PeerLink(ConnectionId id, TransportEndpoint remote)
    {
        public ConnectionId Id { get; } = id;

        public TransportEndpoint Remote { get; } = remote;

        public NetPeer? Peer { get; set; }

        public bool Closing { get; set; }

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

            owner._events?.OnConnected(link.Id);
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            // Peers that never became connections (rejected requests) carry no link; closed-by-us links were reported already.
            if (peer.Tag is PeerLink link && !link.Closing && owner._connections.Remove(link.Id.Value))
            {
                owner._events?.OnDisconnected(link.Id, Map(disconnectInfo.Reason));
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
