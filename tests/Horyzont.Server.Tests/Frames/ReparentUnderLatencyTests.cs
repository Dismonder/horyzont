using System.Numerics;

using Horyzont.Client.Frames;
using Horyzont.Client.Movement;
using Horyzont.Client.Replication;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Server.Characters;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Server.Frames;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging.Abstractions;


namespace Horyzont.Server.Tests.Frames;

/// <summary>
/// K5-20 acceptance (criterion 2): random reparents of scripted players (planet ↔ moving or parked ship, with and without a seat) and of
/// a ship (planet ↔ system) under 100–250 ms RTT, 10 ms jitter and 5 % unreliable loss. No rendered jump over 0.5 m beyond the fastest
/// frame point, no error over 0.5 m against the server at the same tick, no sample in a wrong frame.
/// </summary>
public sealed class ReparentUnderLatencyTests(ITestOutputHelper output)
{
    private const int Reparents = 250;
    private const int Players = 4;
    private const double TickRate = 60;
    private const double Limit = 0.5;
    private const ulong MaxCoveredGap = 10;

    /// <summary>Share of remote frames that may fall into a snapshot gap (not measured for error).</summary>
    private const double MaxUncoveredShare = 0.05;

    /// <summary>Share of remote frames that may lie between samples on both sides of a reparent (error not measured).</summary>
    private const double MaxStraddlingShare = 0.10;
    private static readonly SystemId _system = new(1);
    private static readonly FrameRef _home = FrameRef.Of(WorldFactory.HomePlanet);

    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(2001, 100)]
    [InlineData(2002, 150)]
    [InlineData(2003, 200)]
    [InlineData(2004, 250)]
    public async Task Reparent_ThousandRandom_UnderLatency_RemoteObserver_NoJumpOver05m_NoWrongFrame(int seed, int rtt)
    {
        var report = await RunAsync(seed, rtt);
        output.WriteLine($"reparents={report.Reparents} refused={report.Refused} {report.Remote}");
        Assert.True(report.Reparents >= Reparents, $"only {report.Reparents} reparents");
        Assert.True(report.Remote.Frames > 1000, report.Remote.ToString());
        Assert.True(report.Remote.MaxJump <= Limit, report.Remote.ToString());
        Assert.True(report.Remote.MaxError <= Limit, report.Remote.ToString());
        Assert.Equal(0L, report.Remote.ReparentSnaps);
        Assert.True(report.Remote.Uncovered < report.Remote.Frames * MaxUncoveredShare, report.Remote.ToString());
        Assert.True(report.Remote.Straddling < report.Remote.Frames * MaxStraddlingShare, report.Remote.ToString());
        Assert.Equal(0, report.Remote.WrongFrames);
        Assert.Equal(0, report.Remote.WrongFrameApplied);
    }

    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(2011, 100)]
    [InlineData(2012, 150)]
    [InlineData(2013, 200)]
    [InlineData(2014, 250)]
    public async Task Reparent_ThousandRandom_UnderLatency_OwnEntity_NoJumpOver05m_NoWrongFrame(int seed, int rtt)
    {
        var report = await RunAsync(seed, rtt);
        output.WriteLine($"reparents={report.Reparents} refused={report.Refused} {report.Own}");
        Assert.True(report.Reparents >= Reparents, $"only {report.Reparents} reparents");
        Assert.True(report.Own.Frames > 1000, report.Own.ToString());
        Assert.True(report.Own.MaxJump <= Limit, report.Own.ToString());
        Assert.True(report.Own.MaxError <= Limit, report.Own.ToString());
        Assert.Equal(0L, report.Own.ReparentSnaps);
        Assert.Equal(0, report.Own.WrongFrames);
        Assert.Equal(0, report.Own.WrongFrameApplied);
    }

    private static async Task<Report> RunAsync(int seed, int rtt)
    {
        var conditions = new LinkConditions(TimeSpan.FromMilliseconds((rtt / 2.0) - 5), TimeSpan.FromMilliseconds(10), 0.05, 0, seed);
        await using var rig = await ReplicationRig.StartAsync(conditions: conditions);
        var state = rig.Runtime.State;
        var graph = new FrameGraph(new WorldStateFramePoses(state, new HomeOnlyPlacement(WorldFactory.HomePlanet)));
        var history = new ServerFrameHistory(state, graph);
        var transitions = new FrameTransitions(rig.Runtime.Simulation, history);
        var moving = rig.AddShip(new Vector3(100f, 4010f, 0f));
        var parked = rig.AddShip(new Vector3(0f, 4005f, 100f));
        var drifter = rig.AddShip(new Vector3(0f, 4100f, -60f));
        var script = new ShipCircleScript(new Vector3(0f, 4010f, 0f), 100f);
        var truth = new Truth(graph);
        var driver = new Driver(rig, transitions, truth, new Random(seed), moving, parked, drifter);

        var watcher = new ClientView(await rig.JoinAsync("watcher", 100, new Vector3(0f, 4000f, 0f)), rig.Time);
        var actors = new List<ClientView>();
        for (var i = 0; i < Players; i++)
        {
            var at = new Vector3(-15f + (10f * i), 4000f, 90f);
            var client = await rig.JoinAsync("actor" + i, (ulong)(200 + i), at);
            actors.Add(new ClientView(client, rig.Time));
            driver.Add(client.Player, rig.EntityOf(client.Player), at);
        }

        rig.Runtime.AddTickParticipant(new Participant(ServerTickPhase.Frames, _ => script.Step(rig.Runtime.Simulation, moving)));
        rig.Runtime.AddTickParticipant(new Participant(ServerTickPhase.Frames, driver.OnTick));
        rig.Runtime.AddTickParticipant(history);
        rig.Runtime.AddTickParticipant(new Participant(ServerTickPhase.Compose, runtime => truth.Record(runtime.State, driver.Entities)));

        foreach (var actor in actors)
        {
            watcher.Track(actor.Client.Hello!.YourEntity, truth);
            actor.TrackOwn(truth);
        }

        // The rig runs one server tick per 16 ms frame; the extra 2/3 ms keeps the server at the 60 Hz the client clock assumes.
        void Frame()
        {
            rig.Step();
            rig.Time.Advance(TimeSpan.FromTicks(6_667));
        }

        for (var i = 0; i < 120; i++)
        {
            Frame();
        }

        var steps = 0;
        while (driver.Reparents < Reparents && steps++ < 60 * 400)
        {
            Frame();
            watcher.RenderRemote(truth);
            foreach (var actor in actors)
            {
                actor.RenderOwn(truth);
            }
        }

        rig.Step(60);
        var remote = watcher.Remote with { WrongFrameApplied = watcher.Replication.Entities.WrongFrameApplied, Resyncs = watcher.Resyncs };
        var own = actors.Aggregate(new Stats(), (sum, actor) => sum.Merge(actor.Own with
        {
            WrongFrameApplied = actor.Replication.Entities.WrongFrameApplied,
            Resyncs = actor.Resyncs,
        }));
        return new Report(driver.Reparents, driver.Refused, remote with { MaxSpeed = truth.MaxSpeed }, own with { MaxSpeed = truth.MaxSpeed });
    }

    private sealed record Report(int Reparents, int Refused, Stats Remote, Stats Own);

    /// <summary>Jump and error statistics of one perspective.</summary>
    internal sealed record Stats
    {
        public List<double> Jumps { get; init; } = [];

        public int Frames { get; init; }

        public double MaxJump { get; init; }

        public double MaxError { get; init; }

        public int WrongFrames { get; init; }

        public int WrongFrameApplied { get; init; }

        public long ReparentSnaps { get; init; }

        public int Resyncs { get; init; }

        public double MaxSpeed { get; init; }

        public string Worst { get; init; } = "";

        public long Underruns { get; init; }

        public int Uncovered { get; init; }

        public int Straddling { get; init; }

        public ulong MaxSnapshotGap { get; init; }

        /// <summary>Gets the frames that moved along the straight path of a seat teleport (drawn as a slide, not a jump).</summary>
        public int Smears { get; init; }

        /// <summary>Gets the largest move of a smear frame above the speed allowance, metres.</summary>
        public double MaxSmear { get; init; }

        /// <summary>Gets the smear frames that broke the per-teleport bounds (count, monotonic approach, summed moves).</summary>
        public int SmearViolations { get; init; }

        /// <summary>Gets the smear violations, for the failure message.</summary>
        public string FirstSmearViolation { get; init; } = string.Empty;

        /// <summary>Gets the largest teleport distance of the run, metres.</summary>
        public double MaxTeleport { get; init; }

        public Stats Merge(Stats other) => new()
        {
            Jumps = [.. Jumps, .. other.Jumps],
            Frames = Frames + other.Frames,
            MaxJump = Math.Max(MaxJump, other.MaxJump),
            MaxError = Math.Max(MaxError, other.MaxError),
            WrongFrames = WrongFrames + other.WrongFrames,
            WrongFrameApplied = WrongFrameApplied + other.WrongFrameApplied,
            ReparentSnaps = ReparentSnaps + other.ReparentSnaps,
            Resyncs = Resyncs + other.Resyncs,
            Smears = Smears + other.Smears,
            MaxSmear = Math.Max(MaxSmear, other.MaxSmear),
            SmearViolations = SmearViolations + other.SmearViolations,
            FirstSmearViolation = FirstSmearViolation + other.FirstSmearViolation,
            MaxTeleport = Math.Max(MaxTeleport, other.MaxTeleport),
            Worst = other.MaxError > MaxError ? other.Worst : Worst,
        };

        public override string ToString()
        {
            var sorted = Jumps.Order().ToArray();
            var p99 = sorted.Length == 0 ? 0 : sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(sorted.Length * 0.99))];
            return FormattableString.Invariant(
                $"frames={Frames} maxJump={MaxJump:F3} p99Jump={p99:F3} maxError={MaxError:F3} wrongFrames={WrongFrames} wrongFrameApplied={WrongFrameApplied} reparentSnaps={ReparentSnaps} resyncs={Resyncs} vmax={MaxSpeed:F1} underruns={Underruns} uncovered={Uncovered} straddling={Straddling} maxSnapshotGap={MaxSnapshotGap} smears={Smears} maxSmear={MaxSmear:F3} maxTeleport={MaxTeleport:F3} smearViolations={SmearViolations} {FirstSmearViolation} worst: {Worst}");
        }
    }

    internal sealed class Participant(ServerTickPhase phase, Action<ServerRuntime> tick) : IServerTickParticipant
    {
        public ServerTickPhase Phase => phase;

        public void OnTick(ServerRuntime runtime) => tick(runtime);
    }

    /// <summary>Server world positions, frames and sequences of the scripted players per tick.</summary>
    internal sealed class Truth(FrameGraph graph)
    {
        private readonly Dictionary<(EntityId, ulong), (Vector3D World, FrameRef Frame, uint Seq)> _ticks = [];
        private readonly Dictionary<EntityId, Vector3D> _last = [];
        private readonly Dictionary<EntityId, (FrameRef Frame, uint Seq)> _lastFrame = [];
        private readonly Dictionary<(EntityId, ulong), double> _teleports = [];

        public double MaxSpeed { get; private set; }

        /// <summary>
        /// Gets a value indicating whether reparents move the body (K5-25 seats: enter, switch and exit place it at the seat or the exit
        /// point): their displacement is a teleport, kept out of <see cref="MaxSpeed"/> and allowed to a jump over its tick.
        /// </summary>
        public bool SeatTeleports { get; init; }

        /// <summary>
        /// Credits the teleports of an entity at the ticks in (from, to] that are not in <paramref name="credited"/> and whose
        /// post-teleport server position <paramref name="world"/> matches (within <paramref name="tolerance"/> of the server position at
        /// one of the <paramref name="span"/> ticks from the teleport on): each teleport is credited to one frame pair only.
        /// </summary>
        /// <returns>The sum of the credited teleport distances.</returns>
        public double CreditTeleports(
            EntityId entity, double from, double to, in Vector3D world, int span, double tolerance, HashSet<(EntityId, ulong)> credited)
        {
            var sum = 0.0;
            for (var tick = (ulong)Math.Floor(from) + 1; tick <= (ulong)Math.Ceiling(to); tick++)
            {
                if (!_teleports.TryGetValue((entity, tick), out var distance) || credited.Contains((entity, tick)) || !Lands(entity, tick, world, span, tolerance))
                {
                    continue;
                }

                credited.Add((entity, tick));
                sum += distance;
            }

            return sum;
        }

        /// <summary>
        /// True when <paramref name="world"/> lies on the straight path of a teleport of the entity at a tick in (from, to]: between
        /// the server position before it and one of the <paramref name="span"/> positions from it on (a teleport the remote buffer
        /// draws as a slide between the samples around it).
        /// </summary>
        public bool OnTeleportPath(EntityId entity, double from, double to, in Vector3D world, int span, double tolerance) =>
            TryTeleportPath(entity, from, to, world, span, tolerance, out _, out _, out _);

        /// <summary>As <see cref="OnTeleportPath"/>, also giving the matched teleport tick, its distance and landing point.</summary>
        public bool TryTeleportPath(
            EntityId entity, double from, double to, in Vector3D world, int span, double tolerance, out ulong teleportTick, out double distance, out Vector3D landing)
        {
            for (var tick = (ulong)Math.Floor(from) + 1; tick <= (ulong)Math.Ceiling(to); tick++)
            {
                if (!_teleports.TryGetValue((entity, tick), out distance) || !_ticks.TryGetValue((entity, tick - 1), out var before)
                    || !_ticks.TryGetValue((entity, tick), out var landed))
                {
                    continue;
                }

                for (var k = tick; k <= tick + (ulong)span; k++)
                {
                    if (_ticks.TryGetValue((entity, k), out var after)
                        && Vector3D.Distance(before.World, world) + Vector3D.Distance(world, after.World) <= Vector3D.Distance(before.World, after.World) + tolerance)
                    {
                        teleportTick = tick;
                        landing = landed.World;
                        return true;
                    }
                }
            }

            teleportTick = 0;
            distance = 0;
            landing = default;
            return false;
        }

        /// <summary>Gets the largest teleport distance recorded.</summary>
        public double MaxTeleport => _teleports.Count == 0 ? 0 : _teleports.Values.Max();

        private bool Lands(EntityId entity, ulong teleport, in Vector3D world, int span, double tolerance)
        {
            for (var tick = teleport; tick <= teleport + (ulong)span; tick++)
            {
                if (_ticks.TryGetValue((entity, tick), out var at) && Vector3D.Distance(world, at.World) <= tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        public Dictionary<(FrameRef, ulong), PoseD> Frames { get; } = [];

        public void Record(WorldState state, IReadOnlyDictionary<EntityId, PlayerId> entities)
        {
            foreach (var construct in state.Constructs.All)
            {
                if (graph.TryGetWorldPose(FrameRef.Of(construct.Id), out var pose))
                {
                    Frames[(FrameRef.Of(construct.Id), state.CurrentTick.Value)] = pose;
                }
            }

            foreach (var (entity, player) in entities)
            {
                if (!state.Players.TryGet(player, out var p) || !graph.TryGetWorldPose(p.Frame, out var frame))
                {
                    continue;
                }

                var world = FrameMath.TransformPoint(frame, Vector3D.FromFloat(p.Position));
                _ticks[(entity, state.CurrentTick.Value)] = (world, p.Frame, p.ParentSeq);
                var reparented = _lastFrame.TryGetValue(entity, out var lastFrame) && (lastFrame.Frame != p.Frame || lastFrame.Seq != p.ParentSeq);
                if (_last.TryGetValue(entity, out var last))
                {
                    if (SeatTeleports && reparented)
                    {
                        _teleports[(entity, state.CurrentTick.Value)] = Vector3D.Distance(last, world);
                    }
                    else
                    {
                        MaxSpeed = Math.Max(MaxSpeed, Vector3D.Distance(last, world) * TickRate);
                    }
                }

                _last[entity] = world;
                _lastFrame[entity] = (p.Frame, p.ParentSeq);
            }
        }

        public bool TryAt(EntityId entity, ulong tick, out (Vector3D World, FrameRef Frame, uint Seq) value) => _ticks.TryGetValue((entity, tick), out value);

        /// <summary>Server world position at a fractional tick, linear between recorded ticks.</summary>
        public bool TryWorldAt(EntityId entity, double tick, out Vector3D world)
        {
            world = default;
            var floor = (ulong)Math.Floor(tick);
            if (!TryAt(entity, floor, out var a))
            {
                return false;
            }

            if (!TryAt(entity, floor + 1, out var b))
            {
                world = a.World;
                return tick == floor;
            }

            world = Vector3D.Lerp(a.World, b.World, tick - floor);
            return true;
        }
    }

    /// <summary>Scripted players: walk on small circles on the planet, attach to ships, return; a ship hops planet ↔ system.</summary>
    private sealed class Driver(ReplicationRig rig, FrameTransitions transitions, Truth truth, Random random, EntityId moving, EntityId parked, EntityId drifter)
    {
        private readonly Dictionary<PlayerId, (Vector3 Centre, double Phase, long NextReparent)> _players = [];
        private readonly Dictionary<EntityId, PlayerId> _entities = [];
        private long _tick;
        private const double Reach = 50;
        private readonly HashSet<PlayerId> _placed = [];
        private long _nextShipHop = 90;

        public IReadOnlyDictionary<EntityId, PlayerId> Entities => _entities;

        public int Reparents { get; private set; }

        public int Refused { get; private set; }

        public void Add(PlayerId player, EntityId entity, Vector3 centre)
        {
            _players[player] = (centre, random.NextDouble() * 2 * Math.PI, 60 + random.Next(30, 120));
            _entities[entity] = player;
        }

        public void OnTick(ServerRuntime runtime)
        {
            _ = truth;
            _tick++;
            var sim = runtime.Simulation;
            var state = runtime.State;
            foreach (var (entity, player) in _entities)
            {
                var (centre, phase, next) = _players[player];
                if (!state.Players.TryGet(player, out var p))
                {
                    continue;
                }

                if (_placed.Add(player))
                {
                    sim.UpdatePlayerKinematics(player, p.Frame, p.ParentSeq, centre, p.BodyRotation, 0f);
                    state.Players.TryGet(player, out p);
                }

                if (_tick >= next)
                {
                    var tried = Reparent(state, entity, p!);
                    _players[player] = (centre, phase, _tick + (tried ? random.Next(30, 121) : 10));
                    state.Players.TryGet(player, out p);
                }

                var velocity = Vector3.Zero;
                if (p!.Frame.Kind == FrameKind.Planet)
                {
                    // Walk at 2 m/s on a 5 m circle around the current spot of the circle.
                    var angle = phase + (_tick * 2.0 / 5.0 / TickRate);
                    var feet = p.Position + (new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle)) * (2f / (float)TickRate));
                    velocity = new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle)) * 2f;
                    sim.UpdatePlayerKinematics(player, p.Frame, p.ParentSeq, feet, p.BodyRotation, 0f);
                    state.Players.TryGet(player, out p);
                }

                rig.Characters.States[player] = new ServerCharacterState(
                    WorldFactory.HomePlanet, p!.Frame, p.ParentSeq, p.Position, p.BodyRotation, 0f, velocity,
                    p.Frame.Kind == FrameKind.Planet ? MovementMode.Walking : MovementMode.Frozen, 0);
            }

            if (_tick >= _nextShipHop && state.Constructs.TryGet(drifter, out var ship))
            {
                var target = ship.Parent.Kind == FrameKind.System ? _home : FrameRef.Of(_system);
                if (transitions.TryReparent(drifter, target, null, out _, out _))
                {
                    Reparents++;
                }
                else
                {
                    Refused++;
                }

                _nextShipHop = _tick + random.Next(60, 180);
            }
        }

        private bool Near(WorldState state, PlayerState player, EntityId ship) =>
            transitions.Graph.TryGetWorldPose(player.Frame, out var frame) && transitions.Graph.TryGetWorldPose(FrameRef.Of(ship), out var pose)
            && Vector3D.Distance(FrameMath.TransformPoint(frame, Vector3D.FromFloat(player.Position)), pose.Position) <= Reach;

        private bool Reparent(WorldState state, EntityId entity, PlayerState player)
        {
            FrameRef target;
            CellPos? seat = null;
            if (player.Frame.Kind == FrameKind.Construct)
            {
                target = _home;
            }
            else
            {
                // Board only a ship within reach: the snapshot codec quantizes rotations to about 2 mrad, so a long lever arm in a
                // ship frame would show that quantization, not the reparent (see the plan deviations).
                var near = Near(state, player, moving) ? (Near(state, player, parked) && random.Next(2) == 0 ? parked : moving)
                    : Near(state, player, parked) ? parked : default;
                if (near == default)
                {
                    return false;
                }

                var ship = near;
                target = FrameRef.Of(ship);
                if (random.Next(2) == 0 && !SeatTaken(state, ship))
                {
                    seat = new CellPos(0, 0, 0);
                }
            }

            if (transitions.TryReparent(entity, target, seat, out _, out _))
            {
                Reparents++;
            }
            else
            {
                Refused++;
            }

            return true;
        }

        private bool SeatTaken(WorldState state, EntityId ship)
        {
            foreach (var player in _entities.Values)
            {
                if (state.Players.TryGet(player, out var p) && p.Frame == FrameRef.Of(ship) && p.Seat is not null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>One client: its replication, frames and either a remote render loop or its own entity.</summary>
    internal sealed class ClientView : IFullSnapshotSink
    {
        /// <summary>The frame after a credited teleport is at most this far from a post-teleport server position, metres.</summary>
        private const double TeleportLandTolerance = 0.5;

        private bool _clockStarted;
        private ulong _lastSnapshot;
        private readonly Dictionary<EntityId, List<ulong>> _sampleTicks = [];
        private ulong _maxGap;
        private readonly HomeOnlyPlacement _placement = new(WorldFactory.HomePlanet);
        private readonly ClientFrameHistory _history;
        private readonly ClientFrameGraph _graph;
        private readonly RemoteMotionSampleAdapter _adapter;
        private readonly Dictionary<EntityId, RemoteMotionBuffer> _buffers = [];
        private readonly Dictionary<EntityId, (Vector3D World, double Tick)> _previous = [];
        private readonly HashSet<(EntityId, ulong)> _credited = [];
        private readonly Dictionary<(EntityId, ulong), (int Count, double Last, double Moved, double Allowed)> _smearTracks = [];
        private readonly OwnStateValidator _validator;
        private Truth? _truth;
        private OwnState? _own;
        private Stats _remote = new();
        private Stats _ownStats = new();

        public ClientView(RigClient client, TimeProvider time)
        {
            Client = client;
            Replication = new ClientReplication(message =>
            {
                if (message is ResyncRequest)
                {
                    Resyncs++;
                }

                client.Session.Send(message);
            }, new ConstructMirror(time), time);
            client.Observer = Replication.OnMessage;
            _history = new ClientFrameHistory(Replication.Entities, _placement);
            Replication.Decoder.AddSink(_history);
            _graph = new ClientFrameGraph(Replication.Entities, Replication.Constructs, _history, _placement, _system);
            _adapter = new RemoteMotionSampleAdapter(time);
            Replication.Decoder.AddSink(this);
            Replication.Decoder.AddSink(_adapter);
            _validator = OwnStateValidator.FromHello(client.Hello!, NullLogger<OwnStateValidator>.Instance);
        }

        public RigClient Client { get; }

        public ClientReplication Replication { get; }

        public int Resyncs { get; private set; }

        public Stats Remote => _remote with
        {
            ReparentSnaps = _buffers.Values.Sum(b => b.ReparentSnaps),
            Underruns = _adapter.SampleUnderruns,
            MaxSnapshotGap = _maxGap,
        };

        public Stats Own => _ownStats;

        public void Track(EntityId entity, Truth truth)
        {
            _truth = truth;
            var buffer = new RemoteMotionBuffer(TickRate, frames: _graph);
            _buffers[entity] = buffer;
            _adapter.Register(entity, buffer);
        }

        public void TrackOwn(Truth truth) => _truth = truth;

        public void OnFullSnapshot(Snapshot full)
        {
            if (_lastSnapshot != 0)
            {
                _maxGap = Math.Max(_maxGap, full.ServerTick.Value - _lastSnapshot);
            }

            _lastSnapshot = full.ServerTick.Value;
            if (!_clockStarted)
            {
                // The views attach long after the handshake: start the remote clock at the first snapshot, as a hello received now would.
                _adapter.OnServerHello(Client.Hello! with { ServerTick = full.ServerTick });
                _clockStarted = true;
            }

            // Every sample of a tracked player must be in the frame and sequence the server had at that tick.
            foreach (var entity in full.Entities)
            {
                if (_buffers.ContainsKey(entity.Entity) || (SeatCamera && entity.Entity == Client.Hello!.YourEntity))
                {
                    if (!_sampleTicks.TryGetValue(entity.Entity, out var ticks))
                    {
                        _sampleTicks[entity.Entity] = ticks = [];
                    }

                    ticks.Add(full.ServerTick.Value);
                    if (ticks.Count > 256)
                    {
                        ticks.RemoveRange(0, 128);
                    }
                }

                if ((_buffers.ContainsKey(entity.Entity) || entity.Entity == Client.Hello!.YourEntity) && _truth is not null
                    && _truth.TryAt(entity.Entity, full.ServerTick.Value, out var server)
                    && entity.FrameIndex < full.Frames.Count
                    && (full.Frames[entity.FrameIndex].Frame != server.Frame || entity.ParentSeq != server.Seq))
                {
                    if (entity.Entity == Client.Hello!.YourEntity)
                    {
                        _ownStats = _ownStats with { WrongFrames = _ownStats.WrongFrames + 1 };
                    }
                    else
                    {
                        _remote = _remote with { WrongFrames = _remote.WrongFrames + 1 };
                    }
                }
            }

            if (_validator.Validate(full, out var own) == OwnStateVerdict.Accepted)
            {
                _own = own;
            }
        }

        public void RenderRemote(Truth truth)
        {
            var cursor = _adapter.Clock.RenderTick;
            foreach (var (entity, buffer) in _buffers)
            {
                // Only frames the samples cover: across a snapshot gap (delta chain under loss) every moving entity is extrapolated or
                // interpolated over the gap, which is not a reparent effect.
                if (!Covered(entity, cursor, out var from, out var to))
                {
                    // The previous rendered point is kept: the first frame after the gap measures the jump over it (the speed allowance
                    // of the elapsed time is subtracted), so a snap hidden by a gap is still seen.
                    _remote = _remote with { Uncovered = _remote.Uncovered + 1 };
                    continue;
                }

                if (!buffer.TrySample(cursor, out var sample) || !_graph.TryGetWorldPose(sample.Frame, cursor, out var frame)
                    || !truth.TryWorldAt(entity, cursor, out var server))
                {
                    continue;
                }

                var world = FrameMath.TransformPoint(frame, Vector3D.FromFloat(sample.Position));
                var before = _remote.MaxError;

                // Scripted bodies change their world velocity at the reparent tick (a kink no interpolation can know): between two
                // samples on both sides of a reparent only the jump is measured, the error against the server is not.
                var straddles = !truth.TryAt(entity, from, out var a) || !truth.TryAt(entity, to, out var b) || a.Frame != b.Frame || a.Seq != b.Seq
                    || (truth.SeatTeleports && !SameFrame(truth, entity, cursor, sample.Frame, sample.ParentSeq));
                _remote = Measure(_remote, entity, world, straddles ? null : server, cursor, truth.MaxSpeed);
                if (straddles)
                {
                    _remote = _remote with { Straddling = _remote.Straddling + 1 };
                }

                if (_remote.MaxError > before && _remote.MaxError > 0.5)
                {
                    truth.TryAt(entity, (ulong)cursor, out var at);
                    _remote = _remote with { Worst = FormattableString.Invariant($" e={entity.Value} cursor={cursor:F2} sample={sample.Frame.Kind}:{sample.Frame.Id}/{sample.ParentSeq}@{sample.ServerTick.Value} server={at.Frame.Kind}:{at.Frame.Id}/{at.Seq} count={buffer.Count} world={world} server={server}") };
                }

            }
        }

        private bool Covered(EntityId entity, double cursor, out ulong from, out ulong to)
        {
            from = to = 0;
            if (!_sampleTicks.TryGetValue(entity, out var ticks))
            {
                return false;
            }

            for (var i = ticks.Count - 1; i > 0; i--)
            {
                if (ticks[i - 1] <= cursor)
                {
                    from = ticks[i - 1];
                    to = ticks[i];
                    return to >= cursor && to - from <= MaxCoveredGap;
                }
            }

            return false;
        }

        /// <summary>Gets a value indicating whether an own body in a construct frame is seated and drawn by the seat camera (K5-25).</summary>
        public bool SeatCamera { get; init; }

        public void RenderOwn(Truth truth)
        {
            if (SeatCamera && _own is { Frame.Kind: FrameKind.Construct } seated)
            {
                // K5-25 decision 18: the seated body follows the ship at the render cursor, like the remote passengers.
                var cursor = _adapter.Clock.RenderTick;
                if (!Covered(Client.Hello!.YourEntity, cursor, out _, out _))
                {
                    // Like the remote players: a ship pose across a snapshot gap is extrapolated, not a seat effect.
                    _ownStats = _ownStats with { Uncovered = _ownStats.Uncovered + 1 };
                    return;
                }

                // Before the server seated the body at the cursor (the own state is newer), only the jump is measured.
                if (Horyzont.Client.Ships.SeatView.TryGetSeatedWorldPose(_graph, _graph, seated, null, cursor, out var pose)
                    && truth.TryWorldAt(Client.Hello!.YourEntity, cursor, out var atCursor))
                {
                    var same = SameFrame(truth, Client.Hello!.YourEntity, cursor, seated.Frame, seated.ParentSeq);
                    _ownStats = Measure(_ownStats, Client.Hello!.YourEntity, pose.Position, same ? atCursor : null, cursor, truth.MaxSpeed);
                    if (!same)
                    {
                        _ownStats = _ownStats with { Straddling = _ownStats.Straddling + 1 };
                    }
                }

                return;
            }

            if (_own is not { } own || !_graph.TryGetWorldPose(own.Frame, own.ServerTick.Value, out var frame)
                || !truth.TryWorldAt(Client.Hello!.YourEntity, own.ServerTick.Value, out var server))
            {
                return;
            }

            var world = FrameMath.TransformPoint(frame, Vector3D.FromFloat(own.Feet));
            var before = _ownStats.MaxError;
            _ownStats = Measure(_ownStats, Client.Hello!.YourEntity, world, server, own.ServerTick.Value, truth.MaxSpeed);
            if (_ownStats.MaxError > before && _ownStats.MaxError > 0.3)
            {
                truth.TryAt(Client.Hello!.YourEntity, own.ServerTick.Value, out var at);
                truth.TryAt(Client.Hello!.YourEntity, own.ServerTick.Value - 1, out var prev);
                _ownStats = _ownStats with { Worst = FormattableString.Invariant($"tick={own.ServerTick.Value} own={own.Frame.Kind}:{own.Frame.Id}/{own.ParentSeq} server={at.Frame.Kind}:{at.Frame.Id}/{at.Seq} prev={prev.Frame.Kind}/{prev.Seq} hist={_history.Contains(own.Frame)} client={frame} serverFrame={(truth.Frames.TryGetValue((own.Frame, own.ServerTick.Value), out var sf) ? sf : default)} world={world} server={server}") };
            }
        }

        private static bool SameFrame(Truth truth, EntityId entity, double tick, FrameRef frame, uint seq) =>
            truth.TryAt(entity, (ulong)Math.Floor(tick), out var a) && a.Frame == frame && a.Seq == seq
            && (!truth.TryAt(entity, (ulong)Math.Ceiling(tick), out var b) || (b.Frame == frame && b.Seq == seq));

        private Stats TrackSmear(
            Stats stats, EntityId entity, ulong teleportTick, double teleportDistance, Vector3D landing, Vector3D world, Vector3D previousWorld, int span, double tolerance, double tick, double travel)
        {
            // Per credited teleport: at most span + 1 smear frames, each closer to (or as far from) the landing point than the last,
            // and together moving no more than the teleport distance plus the entity's own travel (v_max over the elapsed time).
            var move = Vector3D.Distance(world, previousWorld);
            var distanceToLanding = Vector3D.Distance(world, landing);
            _smearTracks.TryGetValue((entity, teleportTick), out var track);
            var problem = string.Empty;
            if (track.Count + 1 > span + 1)
            {
                problem = $"count {track.Count + 1} > {span + 1}";
            }
            else if (track.Count > 0 && distanceToLanding > track.Last + tolerance)
            {
                problem = $"moved away {track.Last:F3} -> {distanceToLanding:F3}";
            }
            else if (track.Moved + move > teleportDistance + track.Allowed + travel + tolerance)
            {
                problem = $"moves {track.Moved + move:F3} > teleport {teleportDistance:F3} + travel {track.Allowed + travel:F3}";
            }

            _smearTracks[(entity, teleportTick)] = (track.Count + 1, distanceToLanding, track.Moved + move, track.Allowed + travel);
            return problem.Length == 0
                ? stats
                : stats with
                {
                    SmearViolations = stats.SmearViolations + 1,
                    FirstSmearViolation = stats.FirstSmearViolation
                        + FormattableString.Invariant($"[SMEAR e={entity.Value} teleportTick={teleportTick} tick={tick:F2} {problem}]"),
                };
        }

        private Stats Measure(Stats stats, EntityId entity, Vector3D world, Vector3D? server, double tick, double maxSpeed)
        {
            var error = server is { } truth ? Vector3D.Distance(world, truth) : 0;
            if (_truth is { SeatTeleports: true } && error > 0.5 && error > stats.MaxError)
            {
                _truth.TryAt(entity, (ulong)tick, out var at);
                stats = stats with { Worst = FormattableString.Invariant($"ERROR e={entity.Value} tick={tick:F2} error={error:F2} server={at.Frame.Kind}:{at.Frame.Id}/{at.Seq} world={world} truth={server}") };
            }

            var jump = 0.0;
            if (_previous.TryGetValue(entity, out var previous) && tick >= previous.Tick)
            {
                // A seat teleport may be drawn up to the interpolation delay early (the first sample after an idle stretch, or the
                // newest own state under the seat camera at the render cursor) or late (interpolated over a snapshot gap): one frame
                // pair within that window of a server teleport may move by its distance, and only when the frame after the move lands
                // on the post-teleport server position (review 8: no credit to a smeared or wrong jump, none twice).
                var early = (_adapter.Clock.DelaySeconds * TickRate) + 3;
                var raw = Vector3D.Distance(world, previous.World) - (maxSpeed * (tick - previous.Tick) / TickRate);
                // A frame on the straight path of a teleport (the remote buffer interpolates between the samples around it) is a
                // slide, reported as a smear and not as a jump.
                var span = (int)Math.Ceiling(early);
                var tolerance = TeleportLandTolerance + (maxSpeed / TickRate);
                var teleport = raw > 0 && _truth is { SeatTeleports: true } seats
                    ? seats.CreditTeleports(entity, previous.Tick - early, tick + early, world, span, tolerance, _credited)
                    : 0;
                jump = Math.Max(0, raw - teleport);
                if (jump > 0 && _truth is { SeatTeleports: true } path
                    && path.TryTeleportPath(entity, previous.Tick - early, tick + early, world, span, tolerance, out var teleportTick, out var teleportDistance, out var landing))
                {
                    stats = stats with { Smears = stats.Smears + 1, MaxSmear = Math.Max(stats.MaxSmear, jump) };
                    stats = TrackSmear(stats, entity, teleportTick, teleportDistance, landing, world, previous.World, span, tolerance, tick, maxSpeed * (tick - previous.Tick) / TickRate);
                    jump = 0;
                }

                stats.Jumps.Add(jump);
                if (_truth is { SeatTeleports: true } && jump > 0.5 && jump > stats.MaxJump)
                {
                    _truth.TryAt(entity, (ulong)tick, out var at);
                    stats = stats with { Worst = FormattableString.Invariant($"JUMP e={entity.Value} tick={tick:F2} prev={previous.Tick:F2} teleport={teleport:F2} server={at.Frame.Kind}:{at.Frame.Id}/{at.Seq} world={world} prevWorld={previous.World}") };
                }
            }

            _previous[entity] = (world, tick);
            return stats with
            {
                MaxTeleport = _truth?.MaxTeleport ?? 0,
                Frames = stats.Frames + 1,
                MaxJump = Math.Max(stats.MaxJump, jump),
                MaxError = Math.Max(stats.MaxError, error),
            };
        }
    }
}
