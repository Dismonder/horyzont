using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Replication;

/// <summary>
/// Entity and construct replication of one server (K3-15): interest of players and constructs, reliable spawn and despawn, 20 Hz
/// delta snapshots against an acknowledged baseline, complete construct states in parts, operations restricted to what a recipient
/// may see, and rate-limited resynchronisation. Runs after <c>ServerCharactersBinder</c> in the tick; main thread only.
/// </summary>
public sealed class EntityReplication : IServerTickParticipant, IServerPlayerObserver, IServerClientMessageHandler
{
    /// <summary>Ticks between two interest updates and snapshots (60 Hz / 3 = 20 Hz).</summary>
    public const int SendEveryTicks = 3;

    /// <summary>Resync requests one player may make in any window of one second.</summary>
    public const int MaxResyncsPerSecond = 4;

    private const int AckLogEvery = 64;
    private static readonly TimeSpan _resyncWindow = TimeSpan.FromSeconds(1);

    private readonly ServerSession _session;
    private readonly ServerSimulation _simulation;
    private readonly IEntityReplicationSource _source;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly float _viewRange;
    private readonly Func<PlayerId, bool> _receivesConstructs;
    private readonly ISnapshotBudget _snapshotBudget;
    private readonly IReplicationSendBudget _sendBudget;
    private readonly SnapshotBuilder _snapshots;
    private readonly ConstructStateSender _constructs;
    private readonly Dictionary<PlayerId, PlayerState> _players = [];
    private readonly Dictionary<EntityId, int> _index = [];
    private readonly List<EntityId> _entered = [];
    private readonly List<EntityId> _left = [];
    private readonly byte[] _frame = new byte[1150];
    private ReplicationEntity[] _entities = new ReplicationEntity[64];
    private ReplicationEntity[] _visible = new ReplicationEntity[64];
    private InterestSubject[] _subjects = new InterestSubject[64];
    private InterestViewer[] _viewers = new InterestViewer[8];
    private int _entityCount;
    private bool _forceUpdate;
    private bool _detached;

    /// <summary>Initializes a new instance of the <see cref="EntityReplication"/> class and subscribes to applied operations.</summary>
    /// <param name="session">Session the messages go through.</param>
    /// <param name="simulation">Simulation whose applied operations are replicated.</param>
    /// <param name="source">Authoritative entity copies.</param>
    /// <param name="snapshotBudget">Snapshot byte budget.</param>
    /// <param name="sendBudget">Per-client reliable budget shared with other senders.</param>
    /// <param name="time">Clock of the resync rate limit.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="viewRange">Interest distance in metres, from the bounding sphere surface.</param>
    /// <param name="receivesConstructs">Whether a player gets construct data; false for the local player of a host, who shares the world.</param>
    public EntityReplication(
        ServerSession session, ServerSimulation simulation, IEntityReplicationSource source, ISnapshotBudget snapshotBudget,
        IReplicationSendBudget sendBudget, TimeProvider time, ILogger<EntityReplication> logger, float viewRange = 500f,
        Func<PlayerId, bool>? receivesConstructs = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(snapshotBudget);
        ArgumentNullException.ThrowIfNull(sendBudget);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _session = session;
        _simulation = simulation;
        _source = source;
        _time = time;
        _logger = logger;
        _viewRange = viewRange;
        _receivesConstructs = receivesConstructs ?? (static _ => true);
        _snapshotBudget = snapshotBudget;
        _sendBudget = sendBudget;
        _snapshots = new SnapshotBuilder(snapshotBudget);
        _constructs = new ConstructStateSender(session, simulation.State, sendBudget, logger);
        _simulation.Applied += OnApplied;
    }

    /// <summary>Gets the construct transfers (tests and reports).</summary>
    public ConstructStateSender Constructs => _constructs;

    /// <summary>Gets the number of players with replication state.</summary>
    public int TrackedPlayers => _players.Count;

    /// <summary>Gets the number of entities (without constructs for recipients that do not get them) the player has been told about.</summary>
    /// <param name="player">Player.</param>
    /// <returns>Known entities.</returns>
    public int KnownEntities(PlayerId player) => _players.TryGetValue(player, out var state) ? state.Known.Count : 0;

    /// <summary>Whether the player has been told about the entity.</summary>
    /// <param name="player">Player.</param>
    /// <param name="entity">Entity.</param>
    /// <returns>True when spawned for the player.</returns>
    public bool Knows(PlayerId player, EntityId entity) => _players.TryGetValue(player, out var state) && state.Known.ContainsKey(entity);

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Step(runtime.State.CurrentTick);
    }

    /// <summary>One replication step: interest, lifecycle and snapshots every third tick, transfers every tick.</summary>
    /// <param name="tick">Current tick.</param>
    public void Step(Tick tick)
    {
        if (_detached)
        {
            return;
        }

        var due = tick.Value % SendEveryTicks == 0;
        if (due || _forceUpdate)
        {
            CopyEntities();
            UpdateLifecycle();
            if (due)
            {
                SendSnapshots(tick);
            }

            _forceUpdate = false;
        }

        _constructs.Pump();
    }

    /// <inheritdoc />
    public void OnShutdown(ServerRuntime runtime) => Detach();

    /// <summary>Unsubscribes from the simulation and releases transfers; safe to call more than once.</summary>
    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        _simulation.Applied -= OnApplied;
        _constructs.Clear();
        _players.Clear();
        _index.Clear();
        _entityCount = 0;
    }

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player)
    {
        GetState(player);
        _forceUpdate = true;
    }

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, Horyzont.Core.Protocol.DisconnectCode reason)
    {
        _players.Remove(player);
        _snapshots.Forget(player);
        _constructs.ForgetPlayer(player);
        _snapshotBudget.Forget(player);
        _sendBudget.Forget(player);
    }

    /// <inheritdoc />
    public bool Handle(PlayerId player, Message message)
    {
        switch (message)
        {
            case SnapshotAck ack:
                if (!_snapshots.Acknowledge(player, ack.SnapshotId) && _players.TryGetValue(player, out var state)
                    && state.UnknownAcks++ % AckLogEvery == 0)
                {
                    _logger.UnknownAck(player.Value, ack.SnapshotId, state.UnknownAcks);
                }

                return true;
            case ResyncRequest request:
                HandleResync(player, request.Entity);
                return true;
            default:
                return false;
        }
    }

    private static int Grow<T>(ref T[] array, int needed)
    {
        if (array.Length < needed)
        {
            Array.Resize(ref array, Math.Max(needed, array.Length * 2));
        }

        return needed;
    }

    private PlayerState GetState(PlayerId player)
    {
        if (!_players.TryGetValue(player, out var state))
        {
            state = new PlayerState();
            _players.Add(player, state);
        }

        return state;
    }

    private void CopyEntities()
    {
        while (true)
        {
            _entityCount = _source.CopyEntities(_entities);
            if (_entityCount < _entities.Length)
            {
                break;
            }

            Array.Resize(ref _entities, _entities.Length * 2);
        }

        _index.Clear();
        for (var i = 0; i < _entityCount; i++)
        {
            _index[_entities[i].Id] = i;
        }
    }

    private bool TryFindOwn(PlayerId player, out ReplicationEntity own)
    {
        for (var i = 0; i < _entityCount; i++)
        {
            if (_entities[i].Kind == EntityKind.Player && _entities[i].Owner == player)
            {
                own = _entities[i];
                return true;
            }
        }

        own = default;
        return false;
    }

    private void UpdateLifecycle()
    {
        Grow(ref _subjects, _entityCount);
        for (var i = 0; i < _entityCount; i++)
        {
            ref readonly var entity = ref _entities[i];
            _subjects[i] = new InterestSubject(entity.Id, entity.Frame.Frame, entity.State.Position, entity.BoundsRadius, _viewRange);
        }

        var players = _session.PlayersInGame;
        Grow(ref _viewers, players.Count);
        var viewerCount = 0;
        for (var i = 0; i < players.Count; i++)
        {
            if (TryFindOwn(players[i], out var own))
            {
                _viewers[viewerCount++] = new InterestViewer(players[i], own.Frame.Frame, own.State.Position);
            }
        }

        _session.Interest.Update(_subjects.AsSpan(0, _entityCount), _viewers.AsSpan(0, viewerCount));
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            var state = GetState(player);
            if (!TryFindOwn(player, out _))
            {
                // Without a body there is no viewer, and the interest set is forgotten; resend everything when it returns.
                ResetKnown(player, state);
                continue;
            }

            _session.Interest.GetChanges(player, _entered, _left);
            for (var j = 0; j < _left.Count; j++)
            {
                Leave(player, state, _left[j]);
            }

            for (var j = 0; j < _entered.Count; j++)
            {
                if (_index.TryGetValue(_entered[j], out var index))
                {
                    Enter(player, state, in _entities[index]);
                }
            }
        }
    }

    private void ResetKnown(PlayerId player, PlayerState state)
    {
        if (state.Known.Count == 0)
        {
            return;
        }

        // The client still holds these entities: withdraw them, or they stay as ghosts (collision, raycast) until the body returns.
        foreach (var pair in state.Known)
        {
            if (!state.Removed.Contains(pair.Key))
            {
                _session.Send(player, new EntityDespawn(pair.Key));
            }
        }

        state.Known.Clear();
        state.Mobile.Clear();
        state.Removed.Clear();
        _constructs.ForgetPlayer(player);
    }

    private void Enter(PlayerId player, PlayerState state, in ReplicationEntity entity)
    {
        if (state.Known.ContainsKey(entity.Id))
        {
            return;
        }

        if (entity.Kind == EntityKind.Construct)
        {
            if (!_receivesConstructs(player))
            {
                return;
            }

            _session.Send(player, MakeSpawn(in entity));
            state.Known.Add(entity.Id, entity.Kind);
            _constructs.Enqueue(player, entity.Id);
            return;
        }

        _session.Send(player, MakeSpawn(in entity));
        state.Known.Add(entity.Id, entity.Kind);
        var at = state.Mobile.BinarySearch(entity.Id, EntityIdComparer.Instance);
        state.Mobile.Insert(~at, entity.Id);
    }

    private void Leave(PlayerId player, PlayerState state, EntityId id)
    {
        if (state.Removed.Remove(id))
        {
            // An operation already withdrew this construct with ConstructRemoved.
            return;
        }

        if (!state.Known.Remove(id, out var kind))
        {
            return;
        }

        if (kind == EntityKind.Construct)
        {
            _constructs.Cancel(player, id);
        }
        else
        {
            var at = state.Mobile.BinarySearch(id, EntityIdComparer.Instance);
            if (at >= 0)
            {
                state.Mobile.RemoveAt(at);
            }
        }

        _session.Send(player, new EntityDespawn(id));
    }

    private static EntitySpawn MakeSpawn(in ReplicationEntity entity) =>
        new(entity.Id, entity.Kind, 0, entity.Owner, entity.Frame, entity.State with { FrameIndex = 0, Fields = (EntityStateFields)15 }, []);

    private void SendSnapshots(Tick tick)
    {
        var players = _session.PlayersInGame;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (!_players.TryGetValue(player, out var state) || state.Mobile.Count == 0)
            {
                continue;
            }

            Grow(ref _visible, state.Mobile.Count);
            var count = 0;
            for (var j = 0; j < state.Mobile.Count; j++)
            {
                if (_index.TryGetValue(state.Mobile[j], out var index))
                {
                    _visible[count++] = _entities[index];
                }
            }

            _snapshots.SetEntities(player, _visible.AsSpan(0, count));
            var size = _snapshots.Build(player, tick, _source.GetLastProcessedInputSeq(player), _frame);
            if (size > 0)
            {
                _session.SendEncoded(player, TransportChannel.Unreliable, _frame.AsSpan(0, size));
            }
        }
    }

    private void HandleResync(PlayerId player, EntityId entity)
    {
        if (!_players.TryGetValue(player, out var state))
        {
            return;
        }

        var now = _time.GetTimestamp();
        if (state.ResyncCount == MaxResyncsPerSecond
            && _time.GetElapsedTime(state.ResyncStamps[state.ResyncNext], now) < _resyncWindow)
        {
            _logger.ResyncRefused(player.Value, entity.Value, "rate");
            return;
        }

        state.ResyncStamps[state.ResyncNext] = now;
        state.ResyncNext = (state.ResyncNext + 1) % MaxResyncsPerSecond;
        state.ResyncCount = Math.Min(MaxResyncsPerSecond, state.ResyncCount + 1);
        if (entity.IsNone || !state.Known.TryGetValue(entity, out var kind))
        {
            _logger.ResyncRefused(player.Value, entity.Value, "unknown");
            return;
        }

        if (kind == EntityKind.Construct)
        {
            // A request for a transfer in progress means the client gave up on it (stall): restart with a new part 0.
            _constructs.Enqueue(player, entity);
        }
        else if (_index.TryGetValue(entity, out var index))
        {
            _session.Send(player, MakeSpawn(in _entities[index]));
        }

        _logger.ResyncAccepted(player.Value, entity.Value);
    }

    private void OnApplied(AppliedOperation applied)
    {
        if (_detached || _players.Count == 0)
        {
            return;
        }

        var changes = applied.ConstructChanges;
        if (changes.Count == 0 && applied.Operation is not EntityReparentOp)
        {
            return;
        }

        var message = new OperationApplied(applied.Id, applied.Tick, applied.Operation, changes);
        foreach (var pair in _players)
        {
            var player = pair.Key;
            var state = pair.Value;
            if (state.Known.Count == 0)
            {
                continue;
            }

            if (changes.Count == 0)
            {
                ForwardReparent(player, state, (EntityReparentOp)applied.Operation, message);
            }
            else if (changes.Count == 1)
            {
                ForwardChange(player, state, message, changes[0]);
            }
            else
            {
                // A compound operation never leaks a construct outside the recipient's range: visible ones get their full state.
                for (var i = 0; i < changes.Count; i++)
                {
                    var change = changes[i];
                    if (state.Known.TryGetValue(change.Construct, out var kind) && kind == EntityKind.Construct)
                    {
                        RemoveOrResend(player, state, change);
                    }
                }
            }
        }
    }

    private void ForwardReparent(PlayerId player, PlayerState state, EntityReparentOp op, OperationApplied message)
    {
        if (!state.Known.TryGetValue(op.Entity, out var kind))
        {
            return;
        }

        if (kind == EntityKind.Construct)
        {
            _constructs.Enqueue(player, op.Entity);
        }
        else
        {
            _constructs.SendLive(player, message);
        }
    }

    private void ForwardChange(PlayerId player, PlayerState state, OperationApplied message, ConstructChange change)
    {
        if (!state.Known.TryGetValue(change.Construct, out var kind) || kind != EntityKind.Construct)
        {
            return;
        }

        if (change.Removed)
        {
            // A live copy learns it from the operation; the explicit removal also cancels copies that never saw it.
            _constructs.RouteOperation(player, message, change);
            RemoveOrResend(player, state, change);
            return;
        }

        _constructs.RouteOperation(player, message, change);
    }

    private void RemoveOrResend(PlayerId player, PlayerState state, ConstructChange change)
    {
        if (change.Removed)
        {
            _constructs.Cancel(player, change.Construct);
            state.Known.Remove(change.Construct);
            state.Removed.Add(change.Construct);
            _constructs.SendLive(player, new ConstructRemoved(change.Construct));
        }
        else
        {
            _constructs.Enqueue(player, change.Construct);
        }
    }

    private sealed class EntityIdComparer : IComparer<EntityId>
    {
        public static readonly EntityIdComparer Instance = new();

        public int Compare(EntityId x, EntityId y) => x.Value.CompareTo(y.Value);
    }

    private sealed class PlayerState
    {
        public Dictionary<EntityId, EntityKind> Known { get; } = [];

        public List<EntityId> Mobile { get; } = [];

        public HashSet<EntityId> Removed { get; } = [];

        public long[] ResyncStamps { get; } = new long[MaxResyncsPerSecond];

        public int ResyncNext { get; set; }

        public int ResyncCount { get; set; }

        public int UnknownAcks { get; set; }
    }
}
