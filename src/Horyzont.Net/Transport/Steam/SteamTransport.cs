using System.Buffers;
using System.Runtime.InteropServices;

using Steamworks;

namespace Horyzont.Net.Transport.Steam;

/// <summary>
/// Transport over Steam Networking Sockets (P2P). Assumes the game has called <c>SteamAPI.Init()</c> and calls
/// <c>SteamAPI.RunCallbacks()</c> every frame; connection state changes arrive through those callbacks and are
/// delivered to the game in <see cref="Poll"/>.
/// </summary>
public sealed class SteamTransport : ITransport
{
    private const int ReliableLimit = 512 * 1024;
    private const int UnreliableLimit = 1200;
    private const int SendBufferBytes = 2 * 1024 * 1024;
    private const int BatchSize = 64;

    private readonly TransportOptions _options;
    private readonly Dictionary<uint, Connection> _byHandle = [];
    private readonly Dictionary<ulong, Connection> _byId = [];
    private readonly List<Connection> _outgoing = [];
    private readonly List<Connection> _remoteClosed = [];
    private readonly Queue<PendingEvent> _pending = new();
    private readonly IntPtr[] _messages = new IntPtr[BatchSize];
    private readonly Callback<SteamNetConnectionStatusChangedCallback_t> _statusCallback;
    private readonly byte[] _copyBuffer = new byte[ReliableLimit];
    private IntPtr _sendBuffer;
    private int _sendBufferSize;
    private HSteamListenSocket _listenSocket = HSteamListenSocket.Invalid;
    private HSteamNetPollGroup _pollGroup = HSteamNetPollGroup.Invalid;
    private ulong _nextConnectionId = 1;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="SteamTransport"/> class.</summary>
    /// <param name="options">Timeout settings; Steam handles keep-alive itself.</param>
    public SteamTransport(TransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _statusCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
    }

    /// <inheritdoc />
    /// <remarks>Always true: Steam lingers a closed connection itself and delivers what was queued before the close.</remarks>
    public bool IsDrained => true;

    /// <inheritdoc />
    public int MaxReliablePayload => ReliableLimit;

    /// <inheritdoc />
    public int MaxUnreliablePayload => UnreliableLimit;

    /// <inheritdoc />
    public void Listen(TransportEndpoint endpoint)
    {
        if (endpoint is not SteamEndpoint)
        {
            throw new ArgumentException("Expected a Steam endpoint.", nameof(endpoint));
        }

        if (_listenSocket != HSteamListenSocket.Invalid)
        {
            throw new InvalidOperationException("Already listening.");
        }

        _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 2, BuildConfig());
        _pollGroup = SteamNetworkingSockets.CreatePollGroup();
    }

    /// <inheritdoc />
    public ConnectionId Connect(TransportEndpoint endpoint)
    {
        if (endpoint is not SteamEndpoint steam)
        {
            throw new ArgumentException("Expected a Steam endpoint.", nameof(endpoint));
        }

        var identity = default(SteamNetworkingIdentity);
        identity.SetSteamID64(steam.SteamId);
        var handle = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 2, BuildConfig());
        var connection = Register(handle, steam.SteamId, outgoing: true);
        return new ConnectionId(connection.Id);
    }

    /// <inheritdoc />
    /// <remarks>
    /// When Steam refuses a reliable message (for example <c>k_EResultLimitExceeded</c> because the send buffer is full)
    /// the connection is closed and reported with <see cref="TransportDisconnectReason.TransportError"/>.
    /// </remarks>
    public void Send(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload)
    {
        var limit = channel == TransportChannel.ReliableOrdered ? ReliableLimit : UnreliableLimit;
        if (payload.Length > limit)
        {
            throw new ArgumentException($"Payload of {payload.Length} bytes exceeds the {limit} byte limit of the channel.", nameof(payload));
        }

        if (!_byId.TryGetValue(connection.Value, out var c) || !c.Established || c.Closing || c.CloseReason is not null)
        {
            return;
        }

        EnsureSendBuffer(payload.Length);
        var staging = ArrayPool<byte>.Shared.Rent(payload.Length);
        try
        {
            payload.CopyTo(staging);
            Marshal.Copy(staging, 0, _sendBuffer, payload.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(staging);
        }

        var flags = channel == TransportChannel.ReliableOrdered
            ? Constants.k_nSteamNetworkingSend_Reliable
            : Constants.k_nSteamNetworkingSend_UnreliableNoDelay;
        var result = SteamNetworkingSockets.SendMessageToConnection(c.Handle, _sendBuffer, (uint)payload.Length, flags, out _);
        if (result == EResult.k_EResultOK)
        {
            c.BytesSent += payload.Length;
        }
        else if (channel == TransportChannel.ReliableOrdered && result != EResult.k_EResultNoConnection)
        {
            // A reliable message must never vanish silently (for example when the send buffer is full), so the
            // connection fails instead. A lost unreliable message is within the channel contract.
            Fail(c);
        }
    }

    /// <inheritdoc />
    public void Disconnect(ConnectionId connection)
    {
        if (_byId.TryGetValue(connection.Value, out var c))
        {
            Forget(c);
            c.Closing = true;
            SteamNetworkingSockets.CloseConnection(c.Handle, 0, "disconnect", true);
            _pending.Enqueue(new PendingEvent(PendingKind.Disconnected, c.Id, TransportDisconnectReason.Local));
        }
    }

    /// <inheritdoc />
    public void Poll(ITransportEvents events)
    {
        if (_disposed)
        {
            return;
        }

        while (_pending.TryDequeue(out var pending))
        {
            if (pending.Kind == PendingKind.Connected)
            {
                events.OnConnected(new ConnectionId(pending.Connection));
            }
            else
            {
                events.OnDisconnected(new ConnectionId(pending.Connection), pending.Reason);
            }
        }

        if (_pollGroup != HSteamNetPollGroup.Invalid)
        {
            int count;
            while ((count = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(_pollGroup, _messages, BatchSize)) > 0)
            {
                Dispatch(count, events);
            }
        }

        for (var i = 0; i < _outgoing.Count; i++)
        {
            var c = _outgoing[i];
            int count;
            while (c.Established && (count = SteamNetworkingSockets.ReceiveMessagesOnConnection(c.Handle, _messages, BatchSize)) > 0)
            {
                Dispatch(count, events);
            }
        }

        FinishRemoteClosures(events);
    }

    /// <inheritdoc />
    public TransportStats GetStats(ConnectionId connection)
    {
        if (!_byId.TryGetValue(connection.Value, out var c))
        {
            return default;
        }

        var status = default(SteamNetConnectionRealTimeStatus_t);
        var lanes = default(SteamNetConnectionRealTimeLaneStatus_t);
        var ping = 0f;
        var loss = 0f;
        if (SteamNetworkingSockets.GetConnectionRealTimeStatus(c.Handle, ref status, 0, ref lanes) == EResult.k_EResultOK)
        {
            ping = status.m_nPing;
            loss = status.m_flConnectionQualityLocal >= 0 ? 1f - status.m_flConnectionQualityLocal : 0f;
        }

        return new TransportStats(ping, loss, c.BytesSent, c.BytesReceived);
    }

    /// <inheritdoc />
    public TransportEndpoint GetRemoteEndpoint(ConnectionId connection) =>
        _byId.TryGetValue(connection.Value, out var c)
            ? new SteamEndpoint(c.RemoteSteamId)
            : throw new ArgumentException("Unknown connection.", nameof(connection));

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _statusCallback.Dispose();
        foreach (var c in _byHandle.Values)
        {
            SteamNetworkingSockets.CloseConnection(c.Handle, 0, "shutdown", false);
        }

        if (_listenSocket != HSteamListenSocket.Invalid)
        {
            SteamNetworkingSockets.CloseListenSocket(_listenSocket);
        }

        if (_pollGroup != HSteamNetPollGroup.Invalid)
        {
            SteamNetworkingSockets.DestroyPollGroup(_pollGroup);
        }

        if (_sendBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_sendBuffer);
            _sendBuffer = IntPtr.Zero;
        }

        _byHandle.Clear();
        _byId.Clear();
        _outgoing.Clear();
        _remoteClosed.Clear();
    }

    private SteamNetworkingConfigValue_t[] BuildConfig()
    {
        var timeout = new SteamNetworkingConfigValue_t
        {
            m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutConnected,
            m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
        };
        timeout.m_val.m_int32 = (int)_options.Timeout.TotalMilliseconds;
        var sendBuffer = new SteamNetworkingConfigValue_t
        {
            m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize,
            m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
        };
        sendBuffer.m_val.m_int32 = SendBufferBytes;
        return [timeout, sendBuffer];
    }

    private Connection Register(HSteamNetConnection handle, ulong remoteSteamId, bool outgoing)
    {
        var connection = new Connection(_nextConnectionId++, handle, remoteSteamId, outgoing);
        _byHandle.Add(handle.m_HSteamNetConnection, connection);
        _byId.Add(connection.Id, connection);
        if (outgoing)
        {
            _outgoing.Add(connection);
        }

        return connection;
    }

    private void Forget(Connection connection)
    {
        _byHandle.Remove(connection.Handle.m_HSteamNetConnection);
        _byId.Remove(connection.Id);
        _outgoing.Remove(connection);
    }

    private void EnsureSendBuffer(int length)
    {
        if (_sendBufferSize >= length)
        {
            return;
        }

        if (_sendBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_sendBuffer);
        }

        _sendBufferSize = Math.Max(length, 4096);
        _sendBuffer = Marshal.AllocHGlobal(_sendBufferSize);
    }

    private void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t change)
    {
        if (_disposed)
        {
            return;
        }

        var info = change.m_info;
        if (!_byHandle.TryGetValue(change.m_hConn.m_HSteamNetConnection, out var c))
        {
            if (_listenSocket != HSteamListenSocket.Invalid
                && info.m_hListenSocket == _listenSocket
                && info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
            {
                AcceptIncoming(change.m_hConn, info);
            }

            return;
        }

        switch (info.m_eState)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                if (!c.Established)
                {
                    c.Established = true;
                    c.RemoteSteamId = info.m_identityRemote.GetSteamID64();
                    _pending.Enqueue(new PendingEvent(PendingKind.Connected, c.Id, default));
                }

                break;
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                Close(c, TransportDisconnectReason.Remote);
                break;
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                var timedOut = info.m_eEndReason == (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Misc_Timeout;
                Close(c, timedOut ? TransportDisconnectReason.Timeout : c.Established ? TransportDisconnectReason.TransportError : TransportDisconnectReason.Refused);
                break;
            default:
                break;
        }
    }

    private void AcceptIncoming(HSteamNetConnection handle, SteamNetConnectionInfo_t info)
    {
        if (SteamNetworkingSockets.AcceptConnection(handle) != EResult.k_EResultOK)
        {
            SteamNetworkingSockets.CloseConnection(handle, 0, "accept failed", false);
            return;
        }

        if (!SteamNetworkingSockets.SetConnectionPollGroup(handle, _pollGroup))
        {
            SteamNetworkingSockets.CloseConnection(handle, 0, "poll group failed", false);
            return;
        }

        Register(handle, info.m_identityRemote.GetSteamID64(), outgoing: false);
    }

    private void Close(Connection c, TransportDisconnectReason reason)
    {
        // The handle stays open (and registered) until Poll has delivered the messages still queued on it:
        // closing it here would discard data the peer sent before a graceful disconnect.
        if (c.CloseReason is null && !c.Closing)
        {
            c.CloseReason = reason;
            _remoteClosed.Add(c);
        }
    }

    private void FinishRemoteClosures(ITransportEvents events)
    {
        for (var i = 0; i < _remoteClosed.Count; i++)
        {
            var c = _remoteClosed[i];
            if (c.Closing)
            {
                continue;
            }

            Forget(c);
            c.Closing = true;
            SteamNetworkingSockets.CloseConnection(c.Handle, 0, "closed", false);

            // An incoming connection that never raised OnConnected is not reported either.
            if (c.Established || c.Outgoing)
            {
                events.OnDisconnected(new ConnectionId(c.Id), c.CloseReason!.Value);
            }
        }

        _remoteClosed.Clear();
    }

    private void Fail(Connection c)
    {
        Forget(c);
        c.Closing = true;
        SteamNetworkingSockets.CloseConnection(c.Handle, 0, "send failed", false);
        _pending.Enqueue(new PendingEvent(PendingKind.Disconnected, c.Id, TransportDisconnectReason.TransportError));
    }

    private void Dispatch(int count, ITransportEvents events)
    {
        for (var i = 0; i < count; i++)
        {
            var pointer = _messages[i];
            try
            {
                var message = Marshal.PtrToStructure<SteamNetworkingMessage_t>(pointer);
                if (_byHandle.TryGetValue(message.m_conn.m_HSteamNetConnection, out var c) && c.Established && message.m_cbSize <= _copyBuffer.Length)
                {
                    Marshal.Copy(message.m_pData, _copyBuffer, 0, message.m_cbSize);
                    var reliable = (message.m_nFlags & Constants.k_nSteamNetworkingSend_Reliable) != 0;
                    c.BytesReceived += message.m_cbSize;
                    events.OnReceived(
                        new ConnectionId(c.Id),
                        reliable ? TransportChannel.ReliableOrdered : TransportChannel.Unreliable,
                        _copyBuffer.AsSpan(0, message.m_cbSize));
                }
            }
            finally
            {
                SteamNetworkingMessage_t.Release(pointer);
            }
        }
    }

    private enum PendingKind : byte
    {
        Connected,
        Disconnected,
    }

    private readonly record struct PendingEvent(PendingKind Kind, ulong Connection, TransportDisconnectReason Reason);

    private sealed class Connection(ulong id, HSteamNetConnection handle, ulong remoteSteamId, bool outgoing)
    {
        public ulong Id { get; } = id;

        public HSteamNetConnection Handle { get; } = handle;

        public bool Outgoing { get; } = outgoing;

        public ulong RemoteSteamId { get; set; } = remoteSteamId;

        public bool Established { get; set; }

        public bool Closing { get; set; }

        /// <summary>Gets or sets why the connection ended remotely; it is closed locally once Poll drained its messages.</summary>
        public TransportDisconnectReason? CloseReason { get; set; }

        public long BytesSent { get; set; }

        public long BytesReceived { get; set; }
    }
}
