using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Props;

/// <summary>
/// Replicates the harvested props by region (message 45, K6-31 D17): the full state of every region within
/// <see cref="GameRules.PropSyncEnterMeters"/> of the body of a player when it enters, and again after every operation that changed it.
/// The local player of a host shares the world of the server and gets nothing. Main thread only.
/// </summary>
public sealed class PropReplicator : IServerPlayerObserver, IServerTickParticipant
{
    /// <summary>Ticks between two scans of the regions around the players.</summary>
    public const int ScanEveryTicks = 30;

    /// <summary>Most regions one player gets in one scan; the rest follows with the next scans.</summary>
    public const int MaxRegionsPerPlayerPerScan = 256;

    private readonly Func<ServerSimulation> _simulation;
    private readonly Action<PlayerId, Message> _send;
    private readonly Func<PlayerId, bool> _isLocalPlayer;
    private readonly ILogger<PropReplicator> _logger;

    /// <summary>Initializes a new instance of the <see cref="PropReplicator"/> class.</summary>
    /// <param name="simulation">The simulation of the running world.</param>
    /// <param name="send">Sends a game message to a player of the session.</param>
    /// <param name="isLocalPlayer">True for the local player of a host.</param>
    /// <param name="logger">Logger.</param>
    public PropReplicator(Func<ServerSimulation> simulation, Action<PlayerId, Message> send, Func<PlayerId, bool> isLocalPlayer, ILogger<PropReplicator> logger)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(isLocalPlayer);
        ArgumentNullException.ThrowIfNull(logger);
        _simulation = simulation;
        _send = send;
        _isLocalPlayer = isLocalPlayer;
        _logger = logger;
    }

    /// <summary>Creates the replicator for a server and registers it as player observer and tick participant; call before the world starts.</summary>
    /// <param name="runtime">The server.</param>
    /// <param name="loggers">Loggers.</param>
    /// <returns>The replicator.</returns>
    public static PropReplicator Register(ServerRuntime runtime, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(loggers);
        var replicator = new PropReplicator(
            () => runtime.Simulation, (player, message) => runtime.Session.Send(player, message), runtime.IsLocalPlayer, loggers.CreateLogger<PropReplicator>());
        runtime.AddPlayerObserver(replicator);
        runtime.AddTickParticipant(replicator);
        return replicator;
    }

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player)
    {
        // K6-31 C1: skeleton.
        _ = (_simulation, _send, _isLocalPlayer, _logger);
    }

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason)
    {
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Tick();
    }

    /// <summary>One tick of the replicator.</summary>
    public void Tick()
    {
        _ = _simulation;
    }
}
