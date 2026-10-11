using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Replication;

/// <summary>
/// Entity and construct replication of one server (K3-15): interest of players and constructs, reliable spawn and despawn, 20 Hz
/// delta snapshots against an acknowledged baseline, complete construct states in parts, operations restricted to what a recipient
/// may see, and rate-limited resynchronisation. Runs after <c>ServerCharactersBinder</c> in the tick; main thread only.
/// K5-20: ship-mode constructs are snapshot candidates (the mode is read every round), interest distances are measured in the region of
/// an entity's frame (entities in construct frames inherit the construct's region), and a construct reparent reaches a live copy as
/// an operation instead of a new transfer.
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
    private readonly ReplicationRanges _ranges;
    private readonly Func<PlayerId, bool> _receivesConstructs;
    private readonly ISnapshotBudget _snapshotBudget;
    private readonly IReplicationSendBudget _sendBudget;
    private readonly SnapshotBuilder _snapshots;
    private readonly ConstructStateSender _constructs;
    private readonly FrameGraph _frames;
    private readonly Dictionary<PlayerId, PlayerState> _players = [];
    private readonly Dictionary<EntityId, int> _index = [];
    private readonly List<EntityId> _entered = [];
    private readonly List<EntityId> _left = [];
    private readonly List<EntityId> _withdrawn = [];
    private readonly LifecycleOrder _parentsFirst;
    private readonly LifecycleOrder _childrenFirst;
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
    /// <param name="viewRange">Interest distance in metres for every entity and region, from the bounding sphere surface.</param>
    /// <param name="receivesConstructs">Whether a player gets construct data; false for the local player of a host, who shares the world.</param>
    /// <param name="frames">
    /// Frame graph of the simulation's world for region positions; by default one over the world state without planet placements (a
    /// region position never needs them).
    /// </param>
    public EntityReplication(
        ServerSession session, ServerSimulation simulation, IEntityReplicationSource source, ISnapshotBudget snapshotBudget,
        IReplicationSendBudget sendBudget, TimeProvider time, ILogger<EntityReplication> logger, float viewRange = 500f,
        Func<PlayerId, bool>? receivesConstructs = null, FrameGraph? frames = null)
        : this(session, simulation, source, snapshotBudget, sendBudget, time, logger, ReplicationRanges.Uniform(viewRange), receivesConstructs, frames)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EntityReplication"/> class with interest ranges by region and kind (K5-26).</summary>
    /// <param name="session">Session the messages go through.</param>
    /// <param name="simulation">Simulation whose applied operations are replicated.</param>
    /// <param name="source">Authoritative entity copies.</param>
    /// <param name="snapshotBudget">Snapshot byte budget.</param>
    /// <param name="sendBudget">Per-client reliable budget shared with other senders.</param>
    /// <param name="time">Clock of the resync rate limit.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="ranges">Interest distances by region and kind.</param>
    /// <param name="receivesConstructs">Whether a player gets construct data; false for the local player of a host, who shares the world.</param>
    /// <param name="frames">Frame graph of the simulation's world for region positions (see the other constructor).</param>
    public EntityReplication(
        ServerSession session, ServerSimulation simulation, IEntityReplicationSource source, ISnapshotBudget snapshotBudget,
        IReplicationSendBudget sendBudget, TimeProvider time, ILogger<EntityReplication> logger, ReplicationRanges ranges,
        Func<PlayerId, bool>? receivesConstructs = null, FrameGraph? frames = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(snapshotBudget);
        ArgumentNullException.ThrowIfNull(sendBudget);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(ranges);
        _session = session;
        _simulation = simulation;
        _source = source;
        _time = time;
        _logger = logger;
        _ranges = ranges;
        _receivesConstructs = receivesConstructs ?? (static _ => true);
        _snapshotBudget = snapshotBudget;
        _sendBudget = sendBudget;
        _snapshots = new SnapshotBuilder(snapshotBudget);
        _constructs = new ConstructStateSender(session, simulation.State, sendBudget, logger);
        _frames = frames ?? new FrameGraph(new WorldStateFramePoses(simulation.State, NoPlanetPlacement.Instance));
        _parentsFirst = new LifecycleOrder(this, childrenFirst: false);
        _childrenFirst = new LifecycleOrder(this, childrenFirst: true);
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
            ToRegion(entity.Frame.Frame, entity.State.Position, out var region, out var position);
            _subjects[i] = new InterestSubject(entity.Id, region, position, entity.BoundsRadius, _ranges.For(region, IsShip(in entity)));
        }

        // Passengers have no range of their own (K5-26 decision 2): an entity whose frame chain passes through a ship takes the
        // subject of that ship, so it enters and leaves together with it. Ship subjects never change in this pass.
        for (var i = 0; i < _entityCount; i++)
        {
            if (_entities[i].Kind != EntityKind.Construct && TryFindShip(_entities[i].Frame.Frame, out var ship))
            {
                _subjects[i] = _subjects[ship] with { Entity = _entities[i].Id };
            }
        }

        var players = _session.PlayersInGame;
        Grow(ref _viewers, players.Count);
        var viewerCount = 0;
        for (var i = 0; i < players.Count; i++)
        {
            if (TryFindOwn(players[i], out var own))
            {
                ToRegion(own.Frame.Frame, own.State.Position, out var region, out var position);
                _viewers[viewerCount++] = new InterestViewer(players[i], region, position);
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

            // K5-26 decision 3: children leave before their parents and parents enter before their children (by frame depth, then id).
            _left.Sort(_childrenFirst);
            _entered.Sort(_parentsFirst);
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

    /// <summary>
    /// Position of a point in its frame's region. Planet and system frames are regions themselves (the point as it is); a construct
    /// frame is composed up to its planet or system in double. A frame the graph cannot resolve keeps its own frame as the region, so
    /// only entities in that same frame see it.
    /// </summary>
    private void ToRegion(FrameRef frame, Vector3 local, out FrameRef region, out Vector3 position)
    {
        if (frame.Kind == FrameKind.Construct
            && _frames.TryExpressInRegion(frame, local, out region, out var point) && FrameMath.TryVectorToFloat(point, out position))
        {
            return;
        }

        region = frame;
        position = local;
    }

    private static void AddMobile(PlayerState state, EntityId id)
    {
        var at = state.Mobile.BinarySearch(id, EntityIdComparer.Instance);
        if (at < 0)
        {
            state.Mobile.Insert(~at, id);
        }
    }

    private static void RemoveMobile(PlayerState state, EntityId id)
    {
        var at = state.Mobile.BinarySearch(id, EntityIdComparer.Instance);
        if (at >= 0)
        {
            state.Mobile.RemoveAt(at);
        }
    }

    private bool IsShip(in ReplicationEntity entity) =>
        entity.Kind == EntityKind.Construct
        && _simulation.State.Constructs.TryGet(entity.Id, out var construct) && construct.Mode == ConstructMode.Ship;

    /// <summary>The nearest ship-mode construct in a frame chain (depth ≤ 8), as an index of this round's entities.</summary>
    private bool TryFindShip(FrameRef frame, out int index)
    {
        for (var depth = 0; depth < 8 && frame.Kind == FrameKind.Construct; depth++)
        {
            var id = new EntityId(frame.Id);
            if (!_simulation.State.Constructs.TryGet(id, out var construct))
            {
                break;
            }

            if (construct.Mode == ConstructMode.Ship)
            {
                return _index.TryGetValue(id, out index);
            }

            frame = construct.Parent;
        }

        index = -1;
        return false;
    }

    /// <summary>Whether the entity (when a construct) and every construct of its frame chain have a complete copy at the recipient.</summary>
    private bool IsChainLive(PlayerId player, in ReplicationEntity entity)
    {
        if (entity.Kind == EntityKind.Construct && !_constructs.IsLive(player, entity.Id))
        {
            return false;
        }

        var frame = entity.Frame.Frame;
        for (var depth = 0; depth < 8 && frame.Kind == FrameKind.Construct; depth++)
        {
            var id = new EntityId(frame.Id);
            if (!_constructs.IsLive(player, id) || !_simulation.State.Constructs.TryGet(id, out var construct))
            {
                return false;
            }

            frame = construct.Parent;
        }

        return true;
    }

    private bool IsSnapshotCandidate(in ReplicationEntity entity) =>
        entity.Kind != EntityKind.Construct
        || (_simulation.State.Constructs.TryGet(entity.Id, out var construct) && construct.Mode == ConstructMode.Ship);

    private void ResetKnown(PlayerId player, PlayerState state)
    {
        if (state.Known.Count == 0)
        {
            return;
        }

        // The client still holds these entities: withdraw them, or they stay as ghosts (collision, raycast) until the body returns.
        // Children before their parents (K5-26 decision 3).
        _withdrawn.Clear();
        foreach (var pair in state.Known)
        {
            if (!state.Removed.Contains(pair.Key))
            {
                _withdrawn.Add(pair.Key);
            }
        }

        _withdrawn.Sort(_childrenFirst);
        for (var i = 0; i < _withdrawn.Count; i++)
        {
            _session.Send(player, new EntityDespawn(_withdrawn[i]));
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
            AddMobile(state, entity.Id);
            _constructs.Enqueue(player, entity.Id);
            return;
        }

        _session.Send(player, MakeSpawn(in entity));
        state.Known.Add(entity.Id, entity.Kind);
        AddMobile(state, entity.Id);
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

        RemoveMobile(state, id);
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
            var gated = _receivesConstructs(player);
            for (var j = 0; j < state.Mobile.Count; j++)
            {
                // Constructs are known like players, but only ship-mode ones move outside the journal and go into snapshots (K5-20).
                // K5-26 decision 4: an entity goes into snapshots only once every construct of its chain is live at the recipient.
                if (_index.TryGetValue(state.Mobile[j], out var index) && IsSnapshotCandidate(in _entities[index])
                    && (!gated || IsChainLive(player, in _entities[index])))
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

        if (kind == EntityKind.Construct && !_constructs.IsLive(player, op.Entity))
        {
            // No complete copy yet (a transfer in progress): the transfer restarts with the new header.
            _constructs.Enqueue(player, op.Entity);
        }
        else
        {
            // A live copy follows the reparent as an operation (B1, Y8): never a whole construct state per ship reparent.
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
            RemoveMobile(state, change.Construct);
            state.Removed.Add(change.Construct);
            _constructs.SendLive(player, new ConstructRemoved(change.Construct));
        }
        else
        {
            _constructs.Enqueue(player, change.Construct);
        }
    }

    private sealed class NoPlanetPlacement : IPlanetPlacement
    {
        public static readonly NoPlanetPlacement Instance = new();

        public bool TryGetPose(PlanetId planet, out PoseD pose)
        {
            pose = PoseD.Identity;
            return false;
        }
    }

    /// <summary>Depth of an entity's frame chain in this round: 0 in a planet or system frame, one more per construct frame.</summary>
    private int FrameDepth(EntityId id)
    {
        if (!_index.TryGetValue(id, out var index))
        {
            return 0;
        }

        var frame = _entities[index].Frame.Frame;
        var depth = 0;
        while (depth < 8 && frame.Kind == FrameKind.Construct)
        {
            depth++;
            if (!_simulation.State.Constructs.TryGet(new EntityId(frame.Id), out var construct))
            {
                break;
            }

            frame = construct.Parent;
        }

        return depth;
    }

    /// <summary>Orders lifecycle changes by frame depth (parents first, or children first), then by id; allocation-free.</summary>
    private sealed class LifecycleOrder(EntityReplication owner, bool childrenFirst) : IComparer<EntityId>
    {
        public int Compare(EntityId x, EntityId y)
        {
            var byDepth = owner.FrameDepth(x).CompareTo(owner.FrameDepth(y));
            if (byDepth != 0)
            {
                return childrenFirst ? -byDepth : byDepth;
            }

            return x.Value.CompareTo(y.Value);
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
