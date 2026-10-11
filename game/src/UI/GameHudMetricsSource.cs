using Horyzont.Client.Diagnostics;
using Horyzont.Client.Session;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Boot;
using Horyzont.Net.Session;
using Horyzont.Server;

namespace Horyzont.Game.UI;

/// <summary>
/// Reads the developer HUD metrics of one session through public members of the composition: no reflection, no allocation per read.
/// A host sums the transport counters of its remote players only; a solo process has no network metric; the round-trip time exists
/// for a remote client only. Values that do not exist are reported as unavailable.
/// </summary>
public sealed class GameHudMetricsSource : IHudMetricsSource, IClientSessionListener
{
    private readonly GameComposition _composition;
    private readonly Func<bool> _readOnly;
    private ulong _remoteTick;
    private bool _hasRemoteTick;
    private int _generation;

    /// <summary>Creates the source and starts following the client session of the composition.</summary>
    /// <param name="composition">The session to read.</param>
    /// <param name="readOnly">Confirmed read-only state (from the server status messages).</param>
    public GameHudMetricsSource(GameComposition composition, Func<bool> readOnly)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(readOnly);
        _composition = composition;
        _readOnly = readOnly;
        composition.Client?.AddListener(this);
    }

    /// <inheritdoc />
    public bool IsReadOnly => _readOnly();

    /// <inheritdoc />
    public int SessionGeneration => _generation;

    /// <summary>Stops following the session.</summary>
    public void Detach() => _composition.Client?.RemoveListener(this);

    /// <inheritdoc />
    public bool TryGetServerTick(out ulong tick)
    {
        if (_composition.Server is { RuntimeState: ServerRuntimeState.Running or ServerRuntimeState.SafeMode } server)
        {
            tick = server.State.CurrentTick.Value;
            return true;
        }

        tick = _remoteTick;
        return _composition.Server is null && _hasRemoteTick;
    }

    /// <inheritdoc />
    public bool TryGetServerTickMilliseconds(out double milliseconds)
    {
        milliseconds = _composition.ServerNode?.LastTickMilliseconds ?? double.NaN;
        return double.IsFinite(milliseconds);
    }

    /// <inheritdoc />
    public bool TryGetRtt(out double milliseconds)
    {
        milliseconds = 0;
        if (_composition.Server is not null || _composition.Client is not { } client)
        {
            return false;
        }

        milliseconds = client.Session.Rtt.TotalMilliseconds;
        return milliseconds > 0;
    }

    /// <inheritdoc />
    public bool TryGetTransportBytes(out long sent, out long received)
    {
        sent = 0;
        received = 0;
        if (_composition.Server is { } server)
        {
            if (_composition.Mode != ProcessMode.Host || server.RuntimeState is not (ServerRuntimeState.Running or ServerRuntimeState.SafeMode))
            {
                return false;
            }

            var session = server.Session;
            var players = session.PlayersInGame;
            for (var i = 0; i < players.Count; i++)
            {
                if (server.IsLocalPlayer(players[i]))
                {
                    continue;
                }

                var stats = session.GetTransportStats(players[i]);
                sent += stats.BytesSent;
                received += stats.BytesReceived;
            }

            return true;
        }

        if (_composition.Client is { } client && client.Session.State is not ClientState.Disconnected)
        {
            var stats = client.Session.GetTransportStats();
            sent = stats.BytesSent;
            received = stats.BytesReceived;
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public bool TryGetTools(out int pending, out int rejections)
    {
        pending = 0;
        rejections = 0;
        if (_composition.Tools is not { } tools)
        {
            return false;
        }

        pending = tools.Pending?.Count ?? 0;
        rejections = tools.Rejected;
        return tools.Pending is not null;
    }

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello)
    {
        _generation++;
        _remoteTick = hello.ServerTick.Value;
        _hasRemoteTick = true;
    }

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        if (message is Snapshot snapshot)
        {
            _remoteTick = snapshot.ServerTick.Value;
            _hasRemoteTick = true;
        }
    }

    /// <inheritdoc />
    public void OnDisconnected(Horyzont.Core.Protocol.DisconnectCode code)
    {
        _generation++;
        _hasRemoteTick = false;
    }
}
