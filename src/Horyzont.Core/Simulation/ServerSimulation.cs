using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

using Microsoft.Extensions.Logging;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Authoritative simulation: validates and resolves intents, applies operations and appends them to the journal.
/// After an exception while applying it is faulted: every further call throws and the game stops the server without
/// a checkpoint.
/// </summary>
public sealed class ServerSimulation
{
    private readonly ITerrainService _terrain;
    private readonly IJournalSink _journal;
    private readonly ILogger<ServerSimulation> _logger;
    private readonly OperationApplier _applier;
    private readonly IntentValidator _validator;
    private readonly Dictionary<PlayerId, PlayerBuckets> _buckets = [];
    private readonly List<PlayerBuckets> _bucketList = [];

    /// <summary>Creates the simulation over a state with an empty block content table (no ship has a cockpit or an engine).</summary>
    /// <param name="state">World state.</param>
    /// <param name="terrain">Terrain service.</param>
    /// <param name="journal">Journal sink.</param>
    /// <param name="rules">Game rules.</param>
    /// <param name="logger">Logger.</param>
    public ServerSimulation(WorldState state, ITerrainService terrain, IJournalSink journal, GameRules rules, ILogger<ServerSimulation> logger)
        : this(state, terrain, journal, rules, logger, BlockContentTable.Empty)
    {
    }

    /// <summary>Creates the simulation over a state with the block definitions of the game content.</summary>
    /// <param name="state">World state.</param>
    /// <param name="terrain">Terrain service.</param>
    /// <param name="journal">Journal sink.</param>
    /// <param name="rules">Game rules.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="content">
    /// Block definitions by type id. Read by intent validators only (the ship mode needs cockpits, engines and block masses);
    /// the operation applier never sees it.
    /// </param>
    public ServerSimulation(
        WorldState state, ITerrainService terrain, IJournalSink journal, GameRules rules, ILogger<ServerSimulation> logger,
        BlockContentTable content)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(content);
        State = state;
        Content = content;
        Rules = rules;
        _terrain = terrain;
        _journal = journal;
        _logger = logger;
        _applier = new OperationApplier(state, terrain);
        _validator = new IntentValidator(state, terrain, rules, content);
    }

    /// <summary>World state.</summary>
    public WorldState State { get; }

    /// <summary>Game rules.</summary>
    public GameRules Rules { get; }

    /// <summary>Block definitions the validators read.</summary>
    public BlockContentTable Content { get; }

    /// <summary>
    /// Raised on the calling thread after an operation of <see cref="Submit"/> or <see cref="ApplyServerOperation"/> was applied,
    /// journaled and (for an intent) charged to the limiter (not for <see cref="Replay"/>). A subscriber that throws does not fault the
    /// simulation and does not change the result: the exception is logged and swallowed, because the operation is already journaled.
    /// </summary>
    public event Action<AppliedOperation>? Applied;

    /// <summary>True after an exception while applying an operation.</summary>
    public bool IsFaulted { get; private set; }

    /// <summary>Validates, resolves, applies and journals an intent. Bad input never throws; it is rejected.</summary>
    /// <param name="player">Acting player.</param>
    /// <param name="intent">The intent.</param>
    /// <returns>The result.</returns>
    public IntentResult Submit(PlayerId player, Intent intent)
    {
        EnsureNotFaulted();
        if (intent is null)
        {
            return IntentResult.Rejected(RejectReason.InvalidParameters);
        }

        var buckets = BucketsOf(player);
        var reason = _validator.Validate(player, intent, buckets, out var facts);
        if (reason != RejectReason.None)
        {
            return IntentResult.Rejected(reason);
        }

        var operation = IntentResolver.Resolve(State, intent, facts);
        if (!_applier.CheckPreconditions(operation, out var failure))
        {
            throw new InvalidOperationException($"A validated {operation.Kind} breaks a precondition: {failure}");
        }

        var applied = ApplyAndJournal(player, operation);
        IntentValidator.BucketFor(intent, buckets!)!.Consume();
        if (intent is TerrainToolIntent)
        {
            buckets!.TerrainWindow.Record(State.CurrentTick.Value);
        }

        RaiseApplied(applied);
        return IntentResult.Accepted(applied);
    }

    /// <summary>
    /// Prepares the join of an identity. A known identity gets its player id, entity, frame and last position;
    /// a new one gets <see cref="PlayerIdAllocator.PeekNext"/>, a new entity and the spawn point.
    /// </summary>
    /// <param name="identity">Platform identity.</param>
    /// <param name="displayName">Display name, 1..32 UTF-8 bytes.</param>
    /// <returns>The operation to apply with <see cref="ApplyServerOperation"/>.</returns>
    public PlayerJoinOp PreparePlayerJoin(PlatformIdentity identity, string displayName)
    {
        EnsureNotFaulted();
        if (!OperationApplier.IsDisplayName(displayName))
        {
            throw new ArgumentException("Display name must have 1..32 UTF-8 bytes.", nameof(displayName));
        }

        if (State.Players.TryGetByIdentity(identity, out var known) && State.Players.TryGet(known, out var player))
        {
            return new PlayerJoinOp(
                player.Player, identity, displayName, player.Entity, player.Frame, player.ParentSeq, player.Position, player.BodyRotation);
        }

        if (State.Planets.All.Count == 0)
        {
            throw new InvalidOperationException("The world has no planet to spawn on.");
        }

        // K5-21 decision 10: the registry is sorted by id, so the first planet may be a moon; spawn on the home planet.
        if (!State.Planets.TryGet(State.HomePlanet, out var planet))
        {
            throw new InvalidOperationException("The home planet is not in the world.");
        }

        return new PlayerJoinOp(
            State.PlayerIds.PeekNext(), identity, displayName, State.EntityIds.PeekNext(), FrameRef.Of(planet.Id), 0,
            SpawnPoint(planet), Quaternion.Identity);
    }

    /// <summary>Applies and journals a server operation (join, leave). A broken precondition throws.</summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The applied operation.</returns>
    /// <exception cref="InvalidOperationException">A precondition does not hold; nothing changed.</exception>
    public AppliedOperation ApplyServerOperation(Operation operation)
    {
        EnsureNotFaulted();
        ArgumentNullException.ThrowIfNull(operation);
        if (!_applier.CheckPreconditions(operation, out var failure))
        {
            throw new InvalidOperationException($"{operation.Kind} breaks a precondition: {failure}");
        }

        var applied = ApplyAndJournal(null, operation);
        RaiseApplied(applied);
        return applied;
    }

    /// <summary>Applies a <see cref="PlayerLeaveOp"/> with the current kinematics for every online player, by player id.</summary>
    /// <returns>The applied operations.</returns>
    public IReadOnlyList<AppliedOperation> LeaveAllOnlinePlayers()
    {
        EnsureNotFaulted();
        var online = new List<PlayerState>(State.Players.Online);
        var applied = new List<AppliedOperation>(online.Count);
        foreach (var player in online)
        {
            applied.Add(ApplyServerOperation(new PlayerLeaveOp(
                player.Player, player.Frame, player.ParentSeq, player.Position, player.BodyRotation, player.Pitch)));
        }

        return applied;
    }

    /// <summary>
    /// Replays a journal entry: requires <c>Id == LastOpId.Next</c>, sets CurrentTick to the entry tick and does not
    /// write to the journal. Loads the terrain area before a terrain edit.
    /// A tick before the current one is rejected; a failure while loading or applying faults the simulation.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <exception cref="ReplayFailedException">A gap in operation numbers, a tick going backwards or a broken precondition.</exception>
    public void Replay(JournalEntry entry)
    {
        EnsureNotFaulted();
        ArgumentNullException.ThrowIfNull(entry);
        var kind = entry.Operation.Kind;
        if (entry.Id != State.LastOpId.Next)
        {
            throw new ReplayFailedException(entry.Id, kind, $"expected operation {State.LastOpId.Next.Value}");
        }

        if (entry.Tick < State.CurrentTick)
        {
            throw new ReplayFailedException(entry.Id, kind, $"tick {entry.Tick.Value} is before the current tick {State.CurrentTick.Value}");
        }

        if (!_applier.CheckPreconditions(entry.Operation, out var failure))
        {
            throw new ReplayFailedException(entry.Id, kind, failure);
        }

        try
        {
            if (entry.Operation is TerrainEditOp edit && State.Planets.TryGet(edit.Planet, out var planet))
            {
                _terrain.LoadAreaBlocking(edit.Planet, edit.Center, edit.Radius + (TerrainRules.EditMarginVoxels * planet.VoxelSizeMeters));
            }

            State.SetTick(entry.Tick);
            _applier.Apply(entry.Id, entry.Tick, entry.Actor, entry.Operation);
        }
        catch (Exception exception)
        {
            Fault(exception, entry.Id, kind);
            throw;
        }
    }

    /// <summary>Advances the simulation by one tick and refills the rate limits.</summary>
    public void AdvanceTick()
    {
        EnsureNotFaulted();
        State.SetTick(State.CurrentTick.Next);
        for (var i = 0; i < _bucketList.Count; i++)
        {
            _bucketList[i].Terrain.Refill();
            _bucketList[i].Blocks.Refill();
        }
    }

    /// <summary>
    /// Movement from physics, outside the journal, in the player's frame. False for bad values (logged, previous
    /// kinematics kept) and when the frame and ParentSeq are not current (late movement from before a reparent).
    /// </summary>
    /// <param name="player">Player.</param>
    /// <param name="frame">Frame the values are in.</param>
    /// <param name="parentSeq">Reparent counter the values belong to.</param>
    /// <param name="position">Feet position.</param>
    /// <param name="bodyRotation">Normalized body rotation.</param>
    /// <param name="pitch">Camera pitch.</param>
    /// <returns>True when the kinematics were stored.</returns>
    public bool UpdatePlayerKinematics(PlayerId player, FrameRef frame, uint parentSeq, Vector3 position, Quaternion bodyRotation, float pitch)
    {
        EnsureNotFaulted();
        if (!State.Players.TryGet(player, out var state) || !state.IsOnline || state.Frame != frame || state.ParentSeq != parentSeq)
        {
            return false;
        }

        if (!OperationApplier.IsPose(position, bodyRotation) || !float.IsFinite(pitch))
        {
            SimulationLog.KinematicsRejected(_logger, player.Value);
            return false;
        }

        state.Position = position;
        state.BodyRotation = bodyRotation;
        state.Pitch = pitch;
        return true;
    }

    /// <summary>
    /// Movement of a ship-mode construct frame, outside the journal, in its parent frame (K5-20 decision 6). False for a construct that
    /// is unknown or not in <see cref="ConstructMode.Ship"/>, when the frame and ParentSeq are not current, and for bad values (logged,
    /// previous pose kept). The state hash skips the pose of ship constructs; the checkpoint stores it.
    /// </summary>
    /// <param name="construct">The construct.</param>
    /// <param name="frame">Frame the values are in.</param>
    /// <param name="parentSeq">Reparent counter the values belong to.</param>
    /// <param name="origin">Corner of cell (0,0,0) in <paramref name="frame"/>.</param>
    /// <param name="rotation">Normalized rotation to <paramref name="frame"/>.</param>
    /// <returns>True when the pose was stored.</returns>
    public bool UpdateConstructKinematics(EntityId construct, FrameRef frame, uint parentSeq, Vector3 origin, Quaternion rotation)
    {
        EnsureNotFaulted();
        if (!State.Constructs.TryGet(construct, out var state) || state.Mode != ConstructMode.Ship || state.Parent != frame
            || state.ParentSeq != parentSeq)
        {
            return false;
        }

        if (!OperationApplier.IsPose(origin, rotation))
        {
            SimulationLog.ConstructKinematicsRejected(_logger, construct.Value);
            return false;
        }

        state.SetPose(origin, rotation);
        return true;
    }

    private AppliedOperation ApplyAndJournal(PlayerId? actor, Operation operation)
    {
        var id = State.LastOpId.Next;
        AppliedOperation applied;
        try
        {
            applied = _applier.Apply(id, State.CurrentTick, actor, operation);
            _journal.Append(new JournalEntry(applied.Id, applied.Tick, actor, operation));
        }
        catch (Exception exception)
        {
            Fault(exception, id, operation.Kind);
            throw;
        }

        return applied;
    }

    // After the journal and the limiter bookkeeping: a subscriber (K2-13: the construct view of a host) sees only operations that are
    // applied and journaled; its failure must not undo or fault anything.
    private void RaiseApplied(AppliedOperation applied)
    {
        try
        {
            Applied?.Invoke(applied);
        }
#pragma warning disable CA1031 // Subscriber code is foreign; the operation is already journaled.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            SimulationLog.AppliedSubscriberFailed(_logger, exception, applied.Id.Value);
        }
    }

    private PlayerBuckets? BucketsOf(PlayerId player)
    {
        if (_buckets.TryGetValue(player, out var buckets))
        {
            return buckets;
        }

        if (!State.Players.TryGet(player, out var state) || !state.IsOnline)
        {
            return null;
        }

        buckets = new PlayerBuckets(Rules);
        _buckets.Add(player, buckets);
        _bucketList.Add(buckets);
        return buckets;
    }

    private Vector3 SpawnPoint(PlanetParams planet)
    {
        var top = planet.RadiusMeters + planet.AtmosphereHeightMeters;
        var origin = new Vector3(0f, top, 0f);
        if (_terrain.Raycast(planet.Id, origin, -Vector3.UnitY, top, out var hit))
        {
            return hit.Position + (TerrainMath.UpAt(hit.Position) * 0.1f);
        }

        return new Vector3(0f, planet.RadiusMeters + 0.1f, 0f);
    }

    private void Fault(Exception exception, OpId id, OperationKind kind)
    {
        IsFaulted = true;
        SimulationLog.Faulted(_logger, exception, id.Value, kind);
    }

    private void EnsureNotFaulted()
    {
        if (IsFaulted)
        {
            throw new InvalidOperationException("The simulation is faulted; stop the server without a checkpoint.");
        }
    }
}
