using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Net.Transport;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Net.Session;

/// <summary>
/// Client side of the protocol over an <see cref="ITransport"/>. Malformed or unexpected data from the server, and
/// <see cref="InvalidDataException"/> or <see cref="ProtocolViolationException"/> thrown by the game while applying a
/// message, end the session with <see cref="DisconnectCode.ProtocolViolation"/> instead of escaping <see cref="Poll"/>.
/// Single-threaded: use from the game thread.
/// </summary>
public sealed class ClientSession
{
    /// <summary>Pings awaiting their Pong; older ones are forgotten (a Pong that late is not a useful sample).</summary>
    private const int MaxOutstandingPings = 8;

    private readonly ITransport _transport;
    private readonly ClientOptions _options;
    private readonly IClientGameHooks _hooks;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly TransportSink _sink;
    private readonly ByteWriter _writer = new(2048);
    private readonly ulong[] _outstandingPings = new ulong[MaxOutstandingPings];
    private readonly long _origin;
    private RttEstimator _rtt = new();
    private int _outstandingCount;
    private byte[] _helloFrame = [];
    private ConnectionId _connection;
    private long _connectStarted;
    private long _nextPing;
    private uint _nextIntentSeq;
    private DisconnectCode? _closeCode;
    private DisconnectCode? _remoteCode;
    private bool _notified;

    /// <summary>Initializes a new instance of the <see cref="ClientSession"/> class.</summary>
    /// <param name="transport">Transport; the caller owns it.</param>
    /// <param name="options">Settings.</param>
    /// <param name="hooks">Game callbacks.</param>
    /// <param name="time">Clock of the handshake timeout, pings and round-trip times; only its monotonic timestamp is used.</param>
    /// <param name="logger">Optional logger.</param>
    public ClientSession(ITransport transport, ClientOptions options, IClientGameHooks hooks, TimeProvider time, ILogger<ClientSession>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.PingInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.HandshakeTimeout, TimeSpan.Zero);
        _transport = transport;
        _options = options;
        _hooks = hooks;
        _time = time;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _sink = new TransportSink(this);
        _origin = time.GetTimestamp();
    }

    /// <summary>Gets the session state.</summary>
    public ClientState State { get; private set; }

    /// <summary>
    /// Gets the smoothed round-trip time of the current connection, measured with Ping and the Pong answering it; zero
    /// before the first such Pong. Unsolicited and repeated Pongs are not samples.
    /// </summary>
    public TimeSpan Rtt => _rtt.Smoothed;

    /// <summary>Returns the transport counters of the current connection (bytes are cumulative; all zero when not connected).</summary>
    /// <returns>The statistics.</returns>
    public TransportStats GetTransportStats() => State == ClientState.Disconnected ? default : _transport.GetStats(_connection);

    /// <summary>Gets the number of frames dropped without ending the session (diagnostics and tests).</summary>
    internal long SilentlyDropped { get; private set; }

    /// <summary>Gets the number of frames decoded (diagnostics and tests).</summary>
    internal long FramesDecoded { get; private set; }

    /// <summary>Starts connecting; ClientHello goes out when the transport connects.</summary>
    /// <param name="server">Server endpoint.</param>
    /// <param name="identity">Platform identity of the player.</param>
    /// <param name="displayName">Display name, at most 32 UTF-8 bytes.</param>
    /// <param name="password">Server password, at most 64 UTF-8 bytes.</param>
    /// <param name="authTicket">Platform authentication ticket, at most 1024 bytes.</param>
    /// <exception cref="InvalidOperationException">The session is not <see cref="ClientState.Disconnected"/>.</exception>
    /// <exception cref="ArgumentException">A value exceeds its protocol limit.</exception>
    public void Connect(TransportEndpoint server, PlatformIdentity identity, string displayName, string password = "", byte[]? authTicket = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (State != ClientState.Disconnected)
        {
            throw new InvalidOperationException($"Connect requires the Disconnected state, not {State}.");
        }

        var hello = new ClientHello(ProtocolConstants.ProtocolVersion, _options.BuildHash, identity, displayName, password, authTicket ?? []);
        _writer.Reset();
        MessageCodec.Write(_writer, hello);
        _helloFrame = _writer.WrittenSpan.ToArray();
        _closeCode = null;
        _remoteCode = null;
        _notified = false;
        _nextIntentSeq = 0;
        _rtt = new RttEstimator();
        _outstandingCount = 0;
        _connectStarted = Now();
        State = ClientState.Connecting;
        _connection = _transport.Connect(server);
    }

    /// <summary>Delivers transport events, sends pings and enforces the handshake timeout.</summary>
    public void Poll()
    {
        _transport.Poll(_sink);
        var now = Now();
        if (State is ClientState.Connecting or ClientState.AwaitingServerHello
            && now - _connectStarted >= _options.HandshakeTimeout.Ticks)
        {
            Close(DisconnectCode.HandshakeTimeout, sendMessage: State == ClientState.AwaitingServerHello);
        }
        else if (State is ClientState.Loading or ClientState.InGame && now >= _nextPing)
        {
            _nextPing = now + _options.PingInterval.Ticks;
            var sent = ToMicros(now);
            RememberPing(sent);
            SendMessage(new Ping(sent));
        }
    }

    /// <summary>Tells the server the world is loaded and enters <see cref="ClientState.InGame"/>.</summary>
    /// <exception cref="InvalidOperationException">The session is not <see cref="ClientState.Loading"/>.</exception>
    public void SendReady()
    {
        Require(ClientState.Loading);
        SendMessage(new ClientReady());
        State = ClientState.InGame;
    }

    /// <summary>Submits an intent; the IntentReply arrives through <see cref="IClientGameHooks.OnMessage"/>.</summary>
    /// <param name="intent">The intent.</param>
    /// <returns>The client sequence number carried by the reply.</returns>
    /// <exception cref="InvalidOperationException">The session is not <see cref="ClientState.InGame"/>.</exception>
    public uint SubmitIntent(Intent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        Require(ClientState.InGame);
        var seq = ++_nextIntentSeq;
        SendMessage(new IntentSubmit(seq, intent));
        return seq;
    }

    /// <summary>Sends 1–5 consecutive inputs (the client repeats its recent ones against loss).</summary>
    /// <param name="firstSeq">Number of the first input.</param>
    /// <param name="inputs">The inputs.</param>
    /// <exception cref="InvalidOperationException">The session is not <see cref="ClientState.InGame"/>.</exception>
    public void SendInputs(uint firstSeq, IReadOnlyList<PlayerInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        Require(ClientState.InGame);
        SendMessage(new InputBatch(firstSeq, inputs));
    }

    /// <summary>Sends a game message: TerrainCacheReport (Loading or InGame), TerrainChecksums or SnapshotAck (InGame).</summary>
    /// <param name="message">The message.</param>
    /// <exception cref="ArgumentException">The type is not a client game message.</exception>
    /// <exception cref="InvalidOperationException">The server does not accept the type in the current state.</exception>
    public void Send(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!MessageRouter.IsGameMessageFromClient(message.Type))
        {
            throw new ArgumentException($"{message.Type} is not a client game message.", nameof(message));
        }

        var serverState = State switch
        {
            ClientState.Loading => PeerState.Loading,
            ClientState.InGame => PeerState.InGame,
            _ => PeerState.Closing,
        };
        if (!MessageRouter.IsAllowedFromClient(serverState, message.Type))
        {
            throw new InvalidOperationException($"The server does not accept {message.Type} in state {State}.");
        }

        SendMessage(message);
    }

    /// <summary>Leaves: Disconnect(Normal) to the server, then the transport closes.</summary>
    public void Disconnect() => Close(DisconnectCode.Normal, sendMessage: State is not ClientState.Connecting);

    /// <summary>Sends raw bytes, bypassing all checks (tests of hostile clients).</summary>
    internal void SendRaw(TransportChannel channel, ReadOnlySpan<byte> frame) => _transport.Send(_connection, channel, frame);

    private static ulong ToMicros(long ticks) => (ulong)(ticks / TimeSpan.TicksPerMicrosecond);

    private static TransportChannel ToTransport(DeliveryChannel channel) =>
        channel == DeliveryChannel.ReliableOrdered ? TransportChannel.ReliableOrdered : TransportChannel.Unreliable;

    /// <summary>Monotonic time since construction in <see cref="TimeSpan"/> ticks; wall-clock changes do not move it.</summary>
    private long Now() => _time.GetElapsedTime(_origin).Ticks;

    private void RememberPing(ulong sentMicros)
    {
        if (_outstandingCount == MaxOutstandingPings)
        {
            Array.Copy(_outstandingPings, 1, _outstandingPings, 0, MaxOutstandingPings - 1);
            _outstandingCount--;
        }

        _outstandingPings[_outstandingCount++] = sentMicros;
    }

    /// <summary>Removes the ping a Pong answers.</summary>
    /// <returns>False when no outstanding ping carries <paramref name="sentMicros"/> (unsolicited or repeated Pong).</returns>
    private bool TakePing(ulong sentMicros)
    {
        for (var i = 0; i < _outstandingCount; i++)
        {
            if (_outstandingPings[i] == sentMicros)
            {
                Array.Copy(_outstandingPings, i + 1, _outstandingPings, i, _outstandingCount - i - 1);
                _outstandingCount--;
                return true;
            }
        }

        return false;
    }

    private void Require(ClientState state)
    {
        if (State != state)
        {
            throw new InvalidOperationException($"This call requires the {state} state, not {State}.");
        }
    }

    private void OnConnected(ConnectionId connection)
    {
        if (connection != _connection || State != ClientState.Connecting)
        {
            return;
        }

        _transport.Send(_connection, TransportChannel.ReliableOrdered, _helloFrame);
        State = ClientState.AwaitingServerHello;
    }

    private void OnDisconnected(ConnectionId connection, TransportDisconnectReason reason)
    {
        if (connection != _connection || State == ClientState.Disconnected)
        {
            return;
        }

        var code = reason switch
        {
            TransportDisconnectReason.Local => _closeCode ?? DisconnectCode.Normal,
            TransportDisconnectReason.Remote => _remoteCode ?? DisconnectCode.Normal,
            _ => DisconnectCode.Timeout,
        };
        State = ClientState.Disconnected;
        Notify(code);
    }

    private void OnReceived(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload)
    {
        if (connection != _connection || State is ClientState.Closing or ClientState.Disconnected)
        {
            SilentlyDropped++;
            return;
        }

        try
        {
            Receive(channel, payload);
        }
        catch (ProtocolViolationException ex)
        {
            Violation(ex.Message);
        }
        catch (InvalidDataException ex)
        {
            // Content the codec accepts but the game cannot apply (e.g. duplicate registry ids in ServerHello).
            Violation(ex.Message);
        }
    }

    private void Receive(TransportChannel channel, ReadOnlySpan<byte> payload)
    {
        if (!MessageRouter.TryGetType(payload, out var type))
        {
            Violation(payload.IsEmpty ? "empty frame" : $"unknown message type {payload[0]}");
            return;
        }

        if (ToTransport(MessageChannels.For(type)) != channel)
        {
            Violation($"{type} arrived on the {channel} channel");
            return;
        }

        if (!MessageRouter.IsAllowedFromServer(State, type))
        {
            if (channel == TransportChannel.ReliableOrdered)
            {
                Violation($"{type} is not allowed in state {State}");
            }
            else
            {
                SilentlyDropped++;
            }

            return;
        }

        FramesDecoded++;
        var message = MessageCodec.Read(payload);
        switch (message)
        {
            case ServerHello hello:
                State = ClientState.Loading;
                _nextPing = Now();
                _hooks.OnServerHello(hello);
                break;
            case JoinUnavailable unavailable:
                _hooks.OnMessage(unavailable);
                Close(DisconnectCode.Normal, sendMessage: false);
                break;
            case HandshakeReject reject:
                _hooks.OnRejected(reject.Code, reject.Detail);
                Close(DisconnectCode.Normal, sendMessage: false);
                break;
            case Disconnect disconnect:
                _remoteCode = disconnect.Code;
                Close(disconnect.Code, sendMessage: false);
                break;
            case Pong pong:
                // Only the first Pong answering an outstanding Ping of this connection is a sample (K1-07 review F4).
                if (TakePing(pong.ClientTimeMicros))
                {
                    _rtt.AddSample(TimeSpan.FromMicroseconds(ToMicros(Now()) - pong.ClientTimeMicros));
                }

                _hooks.OnMessage(pong);
                break;
            default:
                _hooks.OnMessage(message);
                break;
        }
    }

    private void Violation(string reason)
    {
        _logger.ServerViolation(reason);
        Close(DisconnectCode.ProtocolViolation, sendMessage: true);
    }

    private void Close(DisconnectCode code, bool sendMessage)
    {
        if (State is ClientState.Closing or ClientState.Disconnected)
        {
            return;
        }

        if (sendMessage)
        {
            SendMessage(new Disconnect(code));
        }

        _closeCode = code;
        State = ClientState.Closing;
        _transport.Disconnect(_connection);
        Notify(code);
    }

    private void Notify(DisconnectCode code)
    {
        if (_notified)
        {
            return;
        }

        _notified = true;
        _logger.ClientDisconnected(code);
        _hooks.OnDisconnected(code);
    }

    private void SendMessage(Message message)
    {
        _writer.Reset();
        MessageCodec.Write(_writer, message);
        _transport.Send(_connection, ToTransport(MessageChannels.For(message.Type)), _writer.WrittenSpan);
    }

    private sealed class TransportSink(ClientSession session) : ITransportEvents
    {
        public void OnConnected(ConnectionId connection) => session.OnConnected(connection);

        public void OnDisconnected(ConnectionId connection, TransportDisconnectReason reason) => session.OnDisconnected(connection, reason);

        public void OnReceived(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload) =>
            session.OnReceived(connection, channel, payload);
    }
}
