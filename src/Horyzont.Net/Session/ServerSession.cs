using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Net.Transport;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Net.Session;

/// <summary>
/// Server side of the protocol over an <see cref="ITransport"/>: handshake, connection states with the messages allowed
/// in each, rate limits, input buffers and interest. Data from the network never throws out of <see cref="Poll"/>:
/// every bad frame ends with a silent drop or a disconnect of its sender. Single-threaded: use from the game thread.
/// </summary>
public sealed class ServerSession
{
    private const int FloodDropLimit = 100;
    private static readonly long _floodWindowTicks = TimeSpan.FromSeconds(5).Ticks;

    private readonly ITransport _transport;
    private readonly ServerOptions _options;
    private readonly IServerGameHooks _hooks;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly TransportSink _sink;
    private readonly ByteWriter _writer = new(4096);
    private readonly Dictionary<ulong, Peer> _byConnection = [];
    private readonly Dictionary<PlayerId, Peer> _byPlayer = [];
    private readonly List<Peer> _peers = [];
    private readonly List<PlayerId> _playersInGame = [];
    private readonly HashSet<PlatformIdentity> _joinedIdentities = [];
    private readonly List<ConnectionId> _refused = [];
    private readonly byte[] _serverFullFrame;
    private readonly byte _clientHelloFormatVersion;
    private readonly long _origin;
    private Tick _currentTick;
    private long _now;
    private bool _shuttingDown;
    private DisconnectCode _shutdownCode;

    /// <summary>Initializes a new instance of the <see cref="ServerSession"/> class.</summary>
    /// <param name="transport">Listening transport; the caller owns it and calls <see cref="ITransport.Listen"/>.</param>
    /// <param name="options">Settings.</param>
    /// <param name="hooks">Game callbacks.</param>
    /// <param name="time">Clock of timeouts and rate limits; only its monotonic timestamp is used.</param>
    /// <param name="logger">Optional logger.</param>
    public ServerSession(ITransport transport, ServerOptions options, IServerGameHooks hooks, TimeProvider time, ILogger<ServerSession>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxPlayers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxPendingConnections);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.InputBufferCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ReliableMessagesPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.UnreliableMessagesPerSecond);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.HandshakeTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.IdleTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.CloseDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(options.InterestHysteresisMeters);
        ArgumentNullException.ThrowIfNull(options.Password);
        ArgumentNullException.ThrowIfNull(options.BannedIdentities);

        _transport = transport;
        _options = options;
        _hooks = hooks;
        _time = time;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _sink = new TransportSink(this);
        Interest = new InterestManager(options.InterestHysteresisMeters);
        _clientHelloFormatVersion = CurrentClientHelloFormatVersion();
        _serverFullFrame = EncodeFrame(new HandshakeReject(HandshakeRejectCode.ServerFull, RejectDetail(HandshakeRejectCode.ServerFull)));
        _origin = time.GetTimestamp();
    }

    /// <summary>Gets the interest sets used by <see cref="SendToInterested"/>; the game calls its Update at 10 Hz.</summary>
    public InterestManager Interest { get; }

    /// <summary>Gets the players in the InGame state, in the order they became ready.</summary>
    public IReadOnlyList<PlayerId> PlayersInGame => _playersInGame;

    /// <summary>Gets a value indicating whether <see cref="Shutdown"/> was called and every connection is closed.</summary>
    public bool IsShutdownComplete => _shuttingDown && _peers.Count == 0;

    /// <summary>Gets the number of connections the session holds state for, in any state (diagnostics and tests).</summary>
    internal int ConnectionCount => _peers.Count;

    /// <summary>Gets the number of frames decoded (diagnostics and tests).</summary>
    internal long FramesDecoded { get; private set; }

    /// <summary>Gets the number of frames dropped without a disconnect: wrong state, rate limit or Closing.</summary>
    internal long SilentlyDropped { get; private set; }

    /// <summary>Delivers transport events, enforces timeouts and closes connections whose close delay elapsed.</summary>
    /// <param name="currentTick">Current simulation tick, sent back in Pong.</param>
    public void Poll(Tick currentTick)
    {
        _currentTick = currentTick;
        _now = Now();
        _transport.Poll(_sink);
        DisconnectRefused();
        _now = Now();

        for (var i = _peers.Count - 1; i >= 0; i--)
        {
            if (i >= _peers.Count)
            {
                continue;
            }

            var peer = _peers[i];
            try
            {
                Maintain(peer);
            }
#pragma warning disable CA1031 // A failing game hook must not take the server down; Fail logs it and closes this peer only.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Fail(peer, ex);
            }
        }
    }

    /// <summary>Sends a game message to a player in the Loading or InGame state; does nothing for other players.</summary>
    /// <param name="player">The player.</param>
    /// <param name="message">A server-to-client game message (not handshake or Disconnect).</param>
    /// <exception cref="ArgumentException">The message type is not a server-to-client game message, or it is too large.</exception>
    public void Send(PlayerId player, Message message)
    {
        EnsureGameMessage(message);
        if (_byPlayer.TryGetValue(player, out var peer))
        {
            SendMessage(peer, message);
        }
    }

    /// <summary>Sends a game message once encoded to every InGame player for whom <paramref name="subject"/> is visible.</summary>
    /// <param name="subject">The entity the message is about.</param>
    /// <param name="message">A server-to-client game message.</param>
    /// <exception cref="ArgumentException">The message type is not a server-to-client game message, or it is too large.</exception>
    public void SendToInterested(EntityId subject, Message message)
    {
        EnsureGameMessage(message);
        var encoded = false;
        var channel = ToTransport(MessageChannels.For(message.Type));
        for (var i = 0; i < _peers.Count; i++)
        {
            var peer = _peers[i];
            if (peer.State != PeerState.InGame || !Interest.IsVisible(peer.Player, subject))
            {
                continue;
            }

            if (!encoded)
            {
                _writer.Reset();
                MessageCodec.Write(_writer, message);
                encoded = true;
            }

            _transport.Send(peer.Connection, channel, _writer.WrittenSpan);
        }
    }

    /// <summary>Ends the session of a player: Disconnect(code), OnPlayerLeft, transport close after the close delay.</summary>
    /// <param name="player">The player.</param>
    /// <param name="code">Reason sent to the client.</param>
    public void Kick(PlayerId player, DisconnectCode code)
    {
        if (_byPlayer.TryGetValue(player, out var peer))
        {
            _now = Now();
            BeginClose(peer, code, sendMessage: true);
        }
    }

    /// <summary>Takes the oldest buffered input of an InGame player; the game calls it once per physics tick.</summary>
    /// <param name="player">The player.</param>
    /// <param name="input">The input.</param>
    /// <param name="seq">Its client sequence number.</param>
    /// <returns>False when no input is buffered or the player is not InGame.</returns>
    public bool TryTakeInput(PlayerId player, out PlayerInput input, out uint seq)
    {
        if (_byPlayer.TryGetValue(player, out var peer) && peer.State == PeerState.InGame && peer.Inputs is { } inputs)
        {
            return inputs.TryTake(out input, out seq);
        }

        input = default;
        seq = 0;
        return false;
    }

    /// <summary>Returns the round-trip time of a player's connection reported by the transport.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The round-trip time, or zero for an unknown player.</returns>
    public TimeSpan GetRtt(PlayerId player) =>
        _byPlayer.TryGetValue(player, out var peer)
            ? TimeSpan.FromMilliseconds(_transport.GetStats(peer.Connection).RttMs)
            : TimeSpan.Zero;

    /// <summary>
    /// Ends every session with <paramref name="code"/> (a leaving host uses <see cref="DisconnectCode.HostLeft"/>) and
    /// refuses new connections the same way. Keep calling <see cref="Poll"/> until <see cref="IsShutdownComplete"/>.
    /// </summary>
    /// <param name="code">Reason sent to every client.</param>
    public void Shutdown(DisconnectCode code)
    {
        _shuttingDown = true;
        _shutdownCode = code;
        _now = Now();
        for (var i = _peers.Count - 1; i >= 0; i--)
        {
            if (i < _peers.Count)
            {
                BeginClose(_peers[i], code, sendMessage: true);
            }
        }
    }

    /// <summary>Returns the transport connection of a connected player (tests).</summary>
    internal ConnectionId ConnectionOf(PlayerId player) => _byPlayer[player].Connection;

    private static TransportChannel ToTransport(DeliveryChannel channel) =>
        channel == DeliveryChannel.ReliableOrdered ? TransportChannel.ReliableOrdered : TransportChannel.Unreliable;

    private static void EnsureGameMessage(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!MessageRouter.IsGameMessageFromServer(message.Type))
        {
            throw new ArgumentException($"{message.Type} is not a server-to-client game message.", nameof(message));
        }
    }

    private static byte[] EncodeFrame(Message message)
    {
        var writer = new ByteWriter(256);
        MessageCodec.Write(writer, message);
        return writer.WrittenSpan.ToArray();
    }

    private static byte CurrentClientHelloFormatVersion()
    {
        // The codec owns the format version; read it from a frame it writes instead of duplicating the constant.
        var writer = new ByteWriter(64);
        MessageCodec.Write(writer, new ClientHello(ProtocolConstants.ProtocolVersion, 0, new PlatformIdentity(IdentityPlatform.Dev, 0), string.Empty, string.Empty, []));
        return writer.WrittenSpan[1];
    }

    private static string RejectDetail(HandshakeRejectCode code) => code switch
    {
        HandshakeRejectCode.WrongProtocol or HandshakeRejectCode.WrongBuild => "The server runs a different version of the game; update the game.",
        HandshakeRejectCode.ServerFull => "The server is full.",
        HandshakeRejectCode.BadPassword => "Wrong password.",
        HandshakeRejectCode.Banned => "You are banned from this server.",
        HandshakeRejectCode.AlreadyConnected => "This identity is already connected.",
        HandshakeRejectCode.IdentityMismatch => "The identity does not match the platform account.",
        _ => "Rejected.",
    };

    /// <summary>Monotonic time since construction in <see cref="TimeSpan"/> ticks; wall-clock changes do not move it.</summary>
    private long Now() => _time.GetElapsedTime(_origin).Ticks;

    private void Maintain(Peer peer)
    {
        switch (peer.State)
        {
            case PeerState.AwaitingHello when _now - peer.ConnectedAt >= _options.HandshakeTimeout.Ticks:
                BeginClose(peer, DisconnectCode.HandshakeTimeout, sendMessage: true);
                break;
            case PeerState.Loading or PeerState.InGame when _now - peer.LastReceived >= _options.IdleTimeout.Ticks:
                BeginClose(peer, DisconnectCode.Timeout, sendMessage: true);
                break;
            case PeerState.Closing when _now >= peer.CloseAt:
                Remove(peer);
                _transport.Disconnect(peer.Connection);
                break;
            default:
                break;
        }
    }

    private void OnConnected(ConnectionId connection)
    {
        if (_byConnection.ContainsKey(connection.Value))
        {
            return;
        }

        // A shutting-down server refuses every newcomer with the shutdown code, whatever the admission bound says (E8).
        if (_shuttingDown)
        {
            _transport.Send(connection, TransportChannel.ReliableOrdered, EncodeFrame(new Disconnect(_shutdownCode)));
            _refused.Add(connection);
            return;
        }

        // Admission before any per-connection state: joined peers are bounded by MaxPlayers, the rest by
        // MaxPendingConnections (K1-07 review F2).
        if (_peers.Count - _byPlayer.Count >= _options.MaxPendingConnections)
        {
            _transport.Send(connection, TransportChannel.ReliableOrdered, _serverFullFrame);
            _refused.Add(connection);
            return;
        }

        var peer = new Peer(connection, _options, _now);
        _byConnection.Add(connection.Value, peer);
        _peers.Add(peer);
    }

    private void OnDisconnected(ConnectionId connection, TransportDisconnectReason reason)
    {
        if (!_byConnection.TryGetValue(connection.Value, out var peer))
        {
            return;
        }

        Remove(peer);
        if (peer.IsJoined)
        {
            // A joined peer is never Closing here, so this side did not close it: either the other side did (Normal, its own
            // code arrives only in a Disconnect message, handled in Dispatch) or the link died.
            var code = reason == TransportDisconnectReason.Remote ? DisconnectCode.Normal : DisconnectCode.Timeout;
            peer.State = PeerState.Closing;
            LeaveGame(peer, code);
        }
    }

    private void DisconnectRefused()
    {
        // Disconnecting while the transport delivers events could disturb its iteration, so refusals wait until here.
        foreach (var connection in _refused)
        {
            _transport.Disconnect(connection);
        }

        _refused.Clear();
    }

    private void OnReceived(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload)
    {
        if (!_byConnection.TryGetValue(connection.Value, out var peer))
        {
            return;
        }

        // The whole per-peer handling is guarded, including rate limiting and violation recovery (K1-07 review F1).
        try
        {
            ReceiveLimited(peer, channel, payload);
        }
#pragma warning disable CA1031 // Bad data or a failing game hook must not take the server down; Fail logs it and closes this peer only.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Fail(peer, ex);
        }
    }

    private void ReceiveLimited(Peer peer, TransportChannel channel, ReadOnlySpan<byte> payload)
    {
        if (peer.State == PeerState.Closing)
        {
            SilentlyDropped++;
            return;
        }

        peer.LastReceived = _now;
        var bucket = channel == TransportChannel.ReliableOrdered ? peer.Reliable : peer.Unreliable;
        if (!bucket.TryTake(_now))
        {
            CountRateDrop(peer);
            return;
        }

        try
        {
            Receive(peer, channel, payload);
        }
        catch (ProtocolViolationException ex)
        {
            Violation(peer, ex.Message);
        }
    }

    private void Receive(Peer peer, TransportChannel channel, ReadOnlySpan<byte> payload)
    {
        // Everything below the type byte is checked before decoding, so a client cannot make the server decode a type it
        // never accepts (K1-04 review F2).
        if (!MessageRouter.TryGetType(payload, out var type))
        {
            Violation(peer, payload.IsEmpty ? "empty frame" : $"unknown message type {payload[0]}");
            return;
        }

        if (ToTransport(MessageChannels.For(type)) != channel)
        {
            Violation(peer, $"{type} arrived on the {channel} channel");
            return;
        }

        if (!MessageRouter.IsAllowedFromClient(peer.State, type))
        {
            if (channel == TransportChannel.ReliableOrdered)
            {
                Violation(peer, $"{type} is not allowed in state {peer.State}");
            }
            else
            {
                SilentlyDropped++;
            }

            return;
        }

        if (type == MessageType.ClientHello)
        {
            if (!MessageCodec.TryReadProtocolVersion(payload, out var version))
            {
                Violation(peer, "truncated ClientHello");
                return;
            }

            if (version != ProtocolConstants.ProtocolVersion || payload[1] != _clientHelloFormatVersion)
            {
                Reject(peer, HandshakeRejectCode.WrongProtocol);
                return;
            }
        }

        FramesDecoded++;
        var message = MessageCodec.Read(payload);
        Dispatch(peer, message);
    }

    private void Dispatch(Peer peer, Message message)
    {
        switch (message)
        {
            case ClientHello hello:
                HandleHello(peer, hello);
                break;
            case ClientReady:
                peer.State = PeerState.InGame;
                _playersInGame.Add(peer.Player);
                _hooks.OnPlayerReady(peer.Player);
                break;
            case IntentSubmit submit:
                var reply = _hooks.OnIntent(peer.Player, submit.ClientSeq, submit.Intent);
                ArgumentNullException.ThrowIfNull(reply);
                if (peer.State == PeerState.InGame)
                {
                    SendMessage(peer, reply.ClientSeq == submit.ClientSeq ? reply : reply with { ClientSeq = submit.ClientSeq });
                }

                break;
            case InputBatch batch:
                peer.Inputs?.Add(batch.FirstSeq, batch.Inputs);
                break;
            case Ping ping:
                SendMessage(peer, new Pong(ping.ClientTimeMicros, _currentTick));
                break;
            case Disconnect disconnect:
                // The game hears Normal whatever the client claims: Kicked, HostLeft and ServerShutdown are codes of this side (E9).
                if (disconnect.Code != DisconnectCode.Normal)
                {
                    _logger.ForeignDisconnectCode(peer.Player.Value, peer.Connection.Value, disconnect.Code);
                }

                BeginClose(peer, DisconnectCode.Normal, sendMessage: false);
                break;
            default:
                // TerrainCacheReport, TerrainChecksums, SnapshotAck: MessageRouter admitted nothing else.
                _hooks.OnClientMessage(peer.Player, message);
                break;
        }
    }

    private void HandleHello(Peer peer, ClientHello hello)
    {
        var identityConnected = _joinedIdentities.Contains(hello.Identity);
        var remote = _transport.GetRemoteEndpoint(peer.Connection);
        var code = HandshakeValidator.Validate(hello, new HandshakeContext(_options, remote, identityConnected, _byPlayer.Count));
        if (code is { } rejected)
        {
            Reject(peer, rejected);
            return;
        }

        var player = _hooks.OnPlayerJoining(hello.Identity, hello.DisplayName);
        if (player.IsNone || _byPlayer.ContainsKey(player))
        {
            throw new InvalidOperationException($"OnPlayerJoining returned an unusable player id {player.Value}.");
        }

        peer.Player = player;
        peer.Identity = hello.Identity;
        peer.State = PeerState.Loading;
        peer.Inputs = new InputBuffer(_options.InputBufferCapacity);
        _byPlayer.Add(player, peer);
        _joinedIdentities.Add(hello.Identity);
        _logger.PlayerJoined(player.Value, peer.Connection.Value);

        // The peer already owns the player, so a throw below closes it through OnPlayerLeft instead of leaving a ghost (E4).
        var serverHello = _hooks.CreateHello(player);
        if (serverHello.YourPlayer != player)
        {
            throw new InvalidOperationException($"CreateHello returned a hello for player {serverHello.YourPlayer.Value}, not {player.Value}.");
        }

        SendMessage(peer, serverHello);
    }

    private void Reject(Peer peer, HandshakeRejectCode code)
    {
        _logger.HandshakeRejected(peer.Connection.Value, code);
        SendMessage(peer, new HandshakeReject(code, RejectDetail(code)));
        peer.State = PeerState.Closing;
        peer.LocalCode = DisconnectCode.Normal;
        peer.CloseAt = _now + _options.CloseDelay.Ticks;
    }

    private void CountRateDrop(Peer peer)
    {
        SilentlyDropped++;
        if (_now - peer.DropWindowStart >= _floodWindowTicks)
        {
            peer.DropWindowStart = _now;
            peer.DropsInWindow = 0;
        }

        peer.DropsInWindow++;
        if (peer.DropsInWindow > FloodDropLimit)
        {
            _logger.Flooding(peer.Player.Value, peer.Connection.Value);
            BeginClose(peer, DisconnectCode.Kicked, sendMessage: true);
        }
    }

    private void Violation(Peer peer, string reason)
    {
        _logger.ProtocolViolation(peer.Player.Value, peer.Connection.Value, reason);
        BeginClose(peer, DisconnectCode.ProtocolViolation, sendMessage: true);
    }

    private void Fail(Peer peer, Exception exception)
    {
        _logger.PeerFailed(peer.Player.Value, peer.Connection.Value, exception);
        try
        {
            BeginClose(peer, DisconnectCode.ProtocolViolation, sendMessage: true);
        }
#pragma warning disable CA1031 // The peer is already Closing (state is set before hooks run); log and keep serving the others.
        catch (Exception inner)
#pragma warning restore CA1031
        {
            _logger.PeerFailed(peer.Player.Value, peer.Connection.Value, inner);
        }
    }

    private void BeginClose(Peer peer, DisconnectCode code, bool sendMessage)
    {
        if (peer.State == PeerState.Closing)
        {
            return;
        }

        var wasJoined = peer.IsJoined;
        peer.State = PeerState.Closing;
        peer.LocalCode = code;
        peer.CloseAt = _now + _options.CloseDelay.Ticks;
        _logger.Closing(peer.Connection.Value, code);
        if (sendMessage)
        {
            SendMessage(peer, new Disconnect(code));
        }

        if (wasJoined)
        {
            LeaveGame(peer, code);
        }
    }

    private void LeaveGame(Peer peer, DisconnectCode code)
    {
        _byPlayer.Remove(peer.Player);
        _joinedIdentities.Remove(peer.Identity);
        _playersInGame.Remove(peer.Player);
        _logger.PlayerLeft(peer.Player.Value, peer.Connection.Value, code);
        try
        {
            _hooks.OnPlayerLeft(peer.Player, code);
        }
#pragma warning disable CA1031 // Cleanup hooks run on every close path (violations, floods, timeouts, Kick, Shutdown); one failure must not stop the others.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LeftHookFailed(peer.Player.Value, peer.Connection.Value, ex);
        }
    }

    private void Remove(Peer peer)
    {
        _byConnection.Remove(peer.Connection.Value);
        _peers.Remove(peer);
    }

    private void SendMessage(Peer peer, Message message)
    {
        _writer.Reset();
        MessageCodec.Write(_writer, message);
        _transport.Send(peer.Connection, ToTransport(MessageChannels.For(message.Type)), _writer.WrittenSpan);
    }

    private sealed class TransportSink(ServerSession session) : ITransportEvents
    {
        public void OnConnected(ConnectionId connection) => session.OnConnected(connection);

        public void OnDisconnected(ConnectionId connection, TransportDisconnectReason reason)
        {
            try
            {
                session.OnDisconnected(connection, reason);
            }
#pragma warning disable CA1031 // A failing OnPlayerLeft hook must not take the server down; the peer is already removed.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                session._logger.PeerFailed(0, connection.Value, ex);
            }
        }

        public void OnReceived(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload) =>
            session.OnReceived(connection, channel, payload);
    }
}
