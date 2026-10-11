using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Space;

namespace Horyzont.Server.Space;

/// <summary>
/// Sends <see cref="StarSystemState"/> (K5-21 decision 14) once per player session when the player becomes ready, only when the
/// world has a star system (otherwise the client stays with the home planet in identity). Control traffic outside the replication
/// budget (README Y5: at most ~560 B once). Main thread only.
/// </summary>
public sealed class ServerSpaceSync : IServerPlayerObserver
{
    private readonly Func<StarSystemParams?> _system;
    private readonly Action<PlayerId, Message> _send;
    private readonly HashSet<PlayerId> _sent = [];
    private StarSystemState? _message;

    /// <summary>Initializes a new instance of the <see cref="ServerSpaceSync"/> class.</summary>
    /// <param name="system">The star system of the running world (read when a player becomes ready).</param>
    /// <param name="send">Sends a message to one player.</param>
    public ServerSpaceSync(Func<StarSystemParams?> system, Action<PlayerId, Message> send)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(send);
        _system = system;
        _send = send;
    }

    /// <summary>Gets the number of <see cref="StarSystemState"/> messages sent.</summary>
    public int Sent { get; private set; }

    /// <summary>Creates the sync for a runtime that has not started yet and registers it as a player observer.</summary>
    /// <param name="runtime">The runtime.</param>
    /// <returns>The sync.</returns>
    public static ServerSpaceSync Register(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var sync = new ServerSpaceSync(() => runtime.State.StarSystem, (player, message) => runtime.Session.Send(player, message));
        runtime.AddPlayerObserver(sync);
        return sync;
    }

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player)
    {
        if (_system() is not { } system || !_sent.Add(player))
        {
            return;
        }

        if (_message is null || !ReferenceEquals(_message.System, system))
        {
            _message = new StarSystemState(system);
        }

        _send(player, _message);
        Sent++;
    }

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason) => _sent.Remove(player);
}
