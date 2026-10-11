using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Server.UI;

/// <summary>Sends an availability snapshot on ready and one message per subsequent transition.</summary>
public sealed class ServerStatusBroadcaster : IServerTickParticipant, IServerPlayerObserver
{
    private readonly Func<ServerStatusInfo> _status;
    private readonly Action<PlayerId, ServerStatus> _send;
    private readonly Dictionary<PlayerId, ServerStatusInfo> _players = [];
    private readonly List<PlayerId> _changed = [];

    /// <summary>Creates a broadcaster with main-thread status and reliable send delegates.</summary>
    public ServerStatusBroadcaster(Func<ServerStatusInfo> status, Action<PlayerId, ServerStatus> send)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(send);
        _status = status;
        _send = send;
    }

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player)
    {
        var status = _status();
        _players[player] = status;
        _send(player, new(status.ReadOnly, status.Reason, status.Detail));
    }

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason) => _players.Remove(player);

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        var status = _status();
        _changed.Clear();
        foreach (var entry in _players)
        {
            if (entry.Value != status)
            {
                _changed.Add(entry.Key);
            }
        }
        if (_changed.Count == 0)
        {
            return;
        }
        var message = new ServerStatus(status.ReadOnly, status.Reason, status.Detail);
        foreach (var player in _changed)
        {
            // Store before callbacks, so a send cannot trigger a duplicate notification.
            _players[player] = status;
            _send(player, message);
        }
    }

    /// <inheritdoc />
    public void OnShutdown(ServerRuntime runtime)
    {
        _players.Clear();
        _changed.Clear();
    }
}
