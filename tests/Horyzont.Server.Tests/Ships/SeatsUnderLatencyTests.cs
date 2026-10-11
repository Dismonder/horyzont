using System.Globalization;
using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Motion;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Server.Characters;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Server.Frames;
using Horyzont.Server.Tests.Frames;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

namespace Horyzont.Server.Tests.Ships;

/// <summary>
/// K5-25 measurement 1 (criterion 1, R10): random seat enter, seat switch, exit and ejection through client intents under 100–250 ms
/// RTT, 10 ms jitter and 5 % unreliable loss, on the K5-20 harness. Ship A stands parked; ship B flies a 20 m/s loop and parks at the
/// same spot between loops. No rendered jump over 0.5 m beyond the fastest point (remote observer, own body with the seat camera), no
/// error over 0.5 m against the server at the same tick, no sample in a wrong frame.
/// </summary>
public sealed class SeatsUnderLatencyTests(ITestOutputHelper output)
{
    private const int Events = 250;
    private const int Players = 4;
    private const double TickRate = 60;
    private const double Limit = 0.5;
    private const int LoopTicks = 360;
    private const int CycleTicks = 600;
    private const float LoopSpeed = 20f;
    private const float WalkSpeed = 4f;
    private static readonly Vector3 _parkA = new(0f, 4000f, 0f);
    private static readonly Vector3 _parkB = new(40f, 4000f, 0f);
    private static readonly Vector3 _nose = new(2.5f, 0f, -2f);
    private static readonly CellPos[] _seats = [new(2, 2, 0), new(1, 2, 1), new(3, 2, 1), new(2, 2, 1)];

    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(2501, 100)]
    [InlineData(2502, 150)]
    [InlineData(2503, 200)]
    [InlineData(2504, 250)]
    public async Task Seats_ThousandRandomEnterExitEject_UnderLatency_NoJumpOver05m_NoWrongFrame(int seed, int rtt)
    {
        var report = await RunAsync(seed, rtt);
        output.WriteLine($"events={report.Events} accepted={report.Accepted} ejects={report.Ejects} rejected=[{report.Rejections}]");
        output.WriteLine($"remote: {report.Remote}");
        output.WriteLine($"own: {report.Own}");
        Assert.True(report.Events >= Events, $"only {report.Events} events");
        Assert.True(report.Accepted >= Events / 2, $"only {report.Accepted} accepted");
        Assert.True(report.Ejects > 0, "no ejection");
        foreach (var stats in new[] { report.Remote, report.Own })
        {
            Assert.True(stats.Frames > 1000, stats.ToString());
            Assert.True(stats.MaxJump <= Limit, stats.ToString());
            Assert.True(stats.MaxError <= Limit, stats.ToString());
            Assert.Equal(0L, stats.ReparentSnaps);
            Assert.Equal(0, stats.WrongFrames);
            Assert.Equal(0, stats.WrongFrameApplied);
        }
    }

    private static async Task<Report> RunAsync(int seed, int rtt)
    {
        var conditions = new LinkConditions(TimeSpan.FromMilliseconds((rtt / 2.0) - 5), TimeSpan.FromMilliseconds(10), 0.05, 0, seed);
        await using var rig = await ReplicationRig.StartAsync(
            () => ShipFixtures.CreateWorld(null), conditions: conditions,
            configure: options => options with { BlockTypeKeys = ShipFixtures.BlockKeys, BlockDefinitions = ShipFixtures.Definitions });
        var state = rig.Runtime.State;
        var sim = rig.Runtime.Simulation;
        var shipA = Build(rig, _parkA);
        var shipB = Build(rig, _parkB);
        var graph = new FrameGraph(new WorldStateFramePoses(state, new HomeOnlyPlacement(WorldFactory.HomePlanet)));
        var history = new ServerFrameHistory(state, graph);
        var truth = new ReparentUnderLatencyTests.Truth(graph) { SeatTeleports = true };
        var driver = new Driver(rig, new Random(seed), shipA, shipB);

        var watcher = new ReparentUnderLatencyTests.ClientView(await rig.JoinAsync("watcher", 100, new Vector3(20f, 4000f, -30f)), rig.Time);
        var actors = new List<ReparentUnderLatencyTests.ClientView>();
        for (var i = 0; i < Players; i++)
        {
            var home = i < Players / 2 ? shipA : shipB;
            var at = Nose(home == shipA ? _parkA : _parkB, i);
            var client = await rig.JoinAsync("actor" + i, (ulong)(200 + i), at);
            actors.Add(new ReparentUnderLatencyTests.ClientView(client, rig.Time) { SeatCamera = true });
            driver.Add(client, rig.EntityOf(client.Player), home, i);
        }

        rig.Runtime.AddTickParticipant(new ReparentUnderLatencyTests.Participant(ServerTickPhase.Frames, driver.OnTick));
        rig.Runtime.AddTickParticipant(history);
        rig.Runtime.AddTickParticipant(new ReparentUnderLatencyTests.Participant(
            ServerTickPhase.Compose, runtime => truth.Record(runtime.State, driver.Entities)));

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
        while (driver.Events < Events && steps++ < 60 * 600)
        {
            driver.Decide(sim);
            Frame();
            watcher.RenderRemote(truth);
            foreach (var actor in actors)
            {
                actor.RenderOwn(truth);
            }
        }

        rig.Step(60);
        var remote = watcher.Remote with { WrongFrameApplied = watcher.Replication.Entities.WrongFrameApplied, Resyncs = watcher.Resyncs };
        var own = actors.Aggregate(new ReparentUnderLatencyTests.Stats(), (sum, actor) => sum.Merge(actor.Own with
        {
            WrongFrameApplied = actor.Replication.Entities.WrongFrameApplied,
            Resyncs = actor.Resyncs,
        }));
        var replies = actors.SelectMany(a => a.Client.Replies).ToList();
        var rejections = string.Join(' ', replies.Where(r => r.Reason != RejectReason.None).GroupBy(r => r.Reason).OrderBy(g => g.Key)
            .Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Key}({(int)g.Key})={g.Count()}")));
        return new Report(
            driver.Events, replies.Count(r => r.Reason == RejectReason.None), driver.Ejects, rejections,
            remote with { MaxSpeed = truth.MaxSpeed }, own with { MaxSpeed = truth.MaxSpeed });
    }

    private static EntityId Build(ReplicationRig rig, Vector3 origin)
    {
        var id = rig.Runtime.State.EntityIds.PeekNext();
        foreach (var op in ShipFixtures.BuildOperations(id, ShipFixtures.Thirty, origin))
        {
            rig.Runtime.Simulation.ApplyServerOperation(op);
        }

        rig.SetMode(id, ConstructMode.Ship);
        return id;
    }

    private static Vector3 Nose(Vector3 park, int index) => park + _nose + new Vector3((index % 2 == 0 ? -0.3f : 0.3f), 0f, 0f);

    private sealed record Report(
        int Events, int Accepted, int Ejects, string Rejections, ReparentUnderLatencyTests.Stats Remote, ReparentUnderLatencyTests.Stats Own);

    /// <summary>
    /// Scripted players: walk to the nose of their ship, enter the cockpit, switch seats, leave or eject; then glide (velocity of the
    /// ship, damped, fall at most 6 m/s) and walk back. Ship B loops at 20 m/s and parks at its spot between loops.
    /// </summary>
    private sealed class Driver(ReplicationRig rig, Random random, EntityId shipA, EntityId shipB)
    {
        private readonly Dictionary<EntityId, PlayerId> _entities = [];
        private readonly Dictionary<PlayerId, Actor> _actors = [];
        private long _tick;

        public IReadOnlyDictionary<EntityId, PlayerId> Entities => _entities;

        public int Events { get; private set; }

        public int Ejects { get; private set; }

        public void Add(RigClient client, EntityId entity, EntityId home, int index)
        {
            _entities[entity] = client.Player;
            _actors[client.Player] = new Actor(client, home, Nose(home == shipA ? _parkA : _parkB, index), 60 + random.Next(30, 120));
        }

        /// <summary>Client side: sends the next intent of every actor whose time came (between frames, through the lossy link).</summary>
        public void Decide(ServerSimulation sim)
        {
            var state = sim.State;
            foreach (var (player, actor) in _actors)
            {
                if (_tick < actor.Next || !state.Players.TryGet(player, out var p))
                {
                    continue;
                }

                var intent = Choose(sim, p, actor);
                actor.Next = _tick + (intent is null ? 10 : random.Next(30, 121));
                if (intent is null)
                {
                    continue;
                }

                if (intent is SeatExitIntent && IsFlying(state, actor.Home))
                {
                    Ejects++;
                }

                actor.Client.Session.SubmitIntent(intent);
                Events++;
            }
        }

        private Intent? Choose(ServerSimulation sim, Core.World.PlayerState p, Actor actor)
        {
            var state = sim.State;
            if (p.Seat is { } seat && p.Frame == FrameRef.Of(actor.Home))
            {
                if (random.Next(2) == 0)
                {
                    return new SeatExitIntent(actor.Home);
                }

                var free = _seats.Where(s => s != seat && !Seated(state, actor.Home, s)).ToArray();
                return free.Length == 0 ? new SeatExitIntent(actor.Home) : Aim(sim, p, actor.Home, free[random.Next(free.Length)]);
            }

            // Standing: only at the nose of the parked ship, with the cockpit free.
            if (p.Frame.Kind != FrameKind.Planet || Vector3.Distance(p.Position, actor.Spot) > 0.25f || IsFlying(state, actor.Home)
                || Seated(state, actor.Home, _seats[0]))
            {
                return null;
            }

            return Aim(sim, p, actor.Home, _seats[0]);
        }

        private static SeatEnterIntent Aim(ServerSimulation sim, Core.World.PlayerState p, EntityId ship, CellPos cell)
        {
            IntentActor.TryGetEye(sim.State, sim.Rules, p, out _, out var eye);
            sim.State.TryToPlanetFrame(FrameRef.Of(ship), new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f), out _, out var target);
            return new SeatEnterIntent(ship, cell, eye, Vector3.Normalize(target - eye));
        }

        private static bool Seated(WorldState state, EntityId ship, CellPos cell) =>
            state.Players.All.Any(p => p.Frame == FrameRef.Of(ship) && p.Seat == cell);

        private static bool IsFlying(WorldState state, EntityId ship) =>
            state.ShipStates.TryGet(ship, out var record) && (record.Flags & ShipStateFlags.Parked) == 0;

        /// <summary>Server tick (frames phase): ship B, then the bodies of the players.</summary>
        public void OnTick(ServerRuntime runtime)
        {
            _tick++;
            var sim = runtime.Simulation;
            var state = runtime.State;
            StepShipB(sim);
            const float dt = (float)(1 / TickRate);
            foreach (var (entity, player) in _entities)
            {
                if (!state.Players.TryGet(player, out var p))
                {
                    continue;
                }

                var actor = _actors[player];
                var mode = MovementMode.Frozen;
                var velocity = Vector3.Zero;
                if (p.Frame.Kind == FrameKind.Planet)
                {
                    if (actor.WasSeated)
                    {
                        // Left the seat: an ejection keeps the velocity of the ship (decision 7), a parked exit starts at rest.
                        actor.Velocity = state.ShipStates.TryGet(actor.Home, out var record) && (record.Flags & ShipStateFlags.Parked) == 0
                            ? record.LinearVelocity : Vector3.Zero;
                        actor.Gliding = actor.Velocity != Vector3.Zero || p.Position.Length() > 4000.05f;
                    }

                    var feet = p.Position;
                    if (actor.Gliding)
                    {
                        var up = TerrainMath.UpAt(feet);
                        var vertical = Vector3.Dot(actor.Velocity, up);
                        var horizontal = (actor.Velocity - (up * vertical)) * (1f - dt);
                        vertical = MathF.Max(vertical - (9.81f * dt), -6f);
                        actor.Velocity = horizontal + (up * vertical);
                        feet += actor.Velocity * dt;
                        if (feet.Length() <= 4000f)
                        {
                            feet = Vector3.Normalize(feet) * 4000f;
                            actor.Gliding = false;
                            actor.Velocity = Vector3.Zero;
                        }

                        mode = actor.Gliding ? MovementMode.Gliding : MovementMode.Walking;
                        velocity = actor.Velocity;
                    }
                    else
                    {
                        var to = actor.Spot - feet;
                        var step = MathF.Min(to.Length(), WalkSpeed * dt);
                        if (step > 1e-4f)
                        {
                            velocity = Vector3.Normalize(to) * WalkSpeed;
                            feet += Vector3.Normalize(to) * step;
                        }

                        mode = MovementMode.Walking;
                    }

                    sim.UpdatePlayerKinematics(player, p.Frame, p.ParentSeq, feet, p.BodyRotation, 0f);
                    state.Players.TryGet(player, out p);
                }

                actor.WasSeated = p!.Frame.Kind == FrameKind.Construct;
                rig.Characters.States[player] = new ServerCharacterState(
                    WorldFactory.HomePlanet, p.Frame, p.ParentSeq, p.Position, p.BodyRotation, 0f, velocity, mode, 0);
            }
        }

        private void StepShipB(ServerSimulation sim)
        {
            if (!sim.State.Constructs.TryGet(shipB, out var ship))
            {
                return;
            }

            var t = _tick % CycleTicks;
            var origin = _parkB;
            var velocity = Vector3.Zero;
            var flying = t < LoopTicks;
            if (flying)
            {
                // One loop of radius 20 m/s · 6 s / 2π per cycle, starting and ending at the parking spot.
                var radius = LoopSpeed * LoopTicks / (float)TickRate / (2 * MathF.PI);
                var omega = LoopSpeed / radius;
                var angle = omega * (float)(t / TickRate);
                origin = _parkB + new Vector3(radius * (1f - MathF.Cos(angle)), 0f, radius * MathF.Sin(angle));
                velocity = new Vector3(radius * omega * MathF.Sin(angle), 0f, radius * omega * MathF.Cos(angle));
            }

            sim.UpdateConstructKinematics(shipB, ship.Parent, ship.ParentSeq, origin, Quaternion.Identity);
            sim.UpdateShipState(shipB, new ShipStateRecord(shipB, 1000f, velocity, Vector3.Zero, flying ? 0 : ShipStateFlags.Parked));
        }

        private sealed class Actor(RigClient client, EntityId home, Vector3 spot, long next)
        {
            public RigClient Client { get; } = client;

            public EntityId Home { get; } = home;

            public Vector3 Spot { get; } = spot;

            public long Next { get; set; } = next;

            public bool WasSeated { get; set; }

            public bool Gliding { get; set; }

            public Vector3 Velocity { get; set; }
        }
    }
}
