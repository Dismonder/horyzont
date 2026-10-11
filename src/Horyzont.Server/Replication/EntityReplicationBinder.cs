using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Replication;

/// <summary>
/// Connects <see cref="EntityReplication"/> to a <see cref="ServerRuntime"/> that has not started yet: the session and the
/// simulation only exist after the world loads, so the replication is created on first use and every call is forwarded to it.
/// Register it with <see cref="Register"/> after the character binder (its movement step must run first) and before
/// <c>StartAsync</c>.
/// </summary>
public sealed class EntityReplicationBinder : IServerTickParticipant, IServerPlayerObserver, IServerClientMessageHandler
{
    private readonly Func<ServerRuntime, IEntityReplicationSource?> _sourceFactory;
    private readonly ISnapshotBudget _snapshotBudget;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;
    private readonly float _viewRange;
    private ServerRuntime? _runtime;
    private readonly Func<ServerRuntime, IShipCorrectionSource?>? _corrections;
    private EntityReplication? _replication;

    /// <summary>Initializes a new instance of the <see cref="EntityReplicationBinder"/> class.</summary>
    /// <param name="sourceFactory">Creates the entity source once the world has started; null while it is not available yet.</param>
    /// <param name="time">Clock of the budgets and the resync limit.</param>
    /// <param name="loggers">Loggers.</param>
    /// <param name="snapshotBudget">Snapshot byte budget; the fixed 1150 bytes when null.</param>
    /// <param name="viewRange">Interest distance in metres (the session's <c>InterestRadiusMeters</c>).</param>
    /// <param name="sendBudget">Reliable budget; the default 27 000 B/s token bucket when null.</param>
    /// <param name="corrections">Ship corrections of pilots (K5-25); null = none.</param>
    public EntityReplicationBinder(
        Func<ServerRuntime, IEntityReplicationSource?> sourceFactory, TimeProvider time, ILoggerFactory loggers, ISnapshotBudget? snapshotBudget = null,
        float viewRange = 500f, IReplicationSendBudget? sendBudget = null, Func<ServerRuntime, IShipCorrectionSource?>? corrections = null)
    {
        _corrections = corrections;
        ArgumentNullException.ThrowIfNull(sourceFactory);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(loggers);
        _sourceFactory = sourceFactory;
        _time = time;
        _loggers = loggers;
        _viewRange = viewRange;
        _snapshotBudget = snapshotBudget ?? new FixedSnapshotBudget();
        SendBudget = sendBudget ?? new TokenBucketReplicationBudget(time);
    }

    /// <summary>Gets the one reliable budget per client, shared with the terrain replication.</summary>
    public IReplicationSendBudget SendBudget { get; }

    /// <summary>Gets the replication once the world has started and the source exists; null before.</summary>
    public EntityReplication? Replication => _replication;

    /// <summary>Registers the binder as tick participant, player observer and client message handler of a server runtime.</summary>
    /// <param name="runtime">The runtime, not started yet.</param>
    public void Register(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        runtime.AddTickParticipant(this);
        runtime.AddPlayerObserver(this);
        runtime.AddClientMessageHandler(this);
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime) => Ensure(runtime)?.OnTick(runtime);

    /// <inheritdoc />
    public void OnShutdown(ServerRuntime runtime) => _replication?.OnShutdown(runtime);

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player) => Ensure(_runtime)?.OnPlayerReady(player);

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason) => _replication?.OnPlayerLeaving(player, reason);

    /// <inheritdoc />
    public bool Handle(PlayerId player, Message message) => Ensure(_runtime)?.Handle(player, message) ?? false;

    private EntityReplication? Ensure(ServerRuntime? runtime)
    {
        if (_replication is not null || runtime is null || runtime.RuntimeState is not (ServerRuntimeState.Running or ServerRuntimeState.SafeMode))
        {
            return _replication;
        }

        var source = _sourceFactory(runtime);
        if (source is null)
        {
            return null;
        }

        _replication = new EntityReplication(
            runtime.Session, runtime.Simulation, source, _snapshotBudget, SendBudget, _time, _loggers.CreateLogger<EntityReplication>(),
            _viewRange, player => !runtime.IsLocalPlayer(player))
        {
            ShipCorrections = _corrections?.Invoke(runtime),
        };
        return _replication;
    }
}
