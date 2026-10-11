using System.Numerics;

using Horyzont.Client.Frames;
using Horyzont.Client.Replication;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Server.Characters;
using Horyzont.Game.Server.Replication;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Server.Frames;
using Horyzont.Server.Replication;
using Horyzont.Server.Ships;
using Horyzont.Server.Space;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Tests.Ships;

/// <summary>
/// K5-26 acceptance harness (in place of the K4-19 <c>CoopRig</c>, which is not on <c>main</c>): a <see cref="ServerRuntime"/>
/// without the engine over the star system of a seed, the K5-24 flight with development pilots, the K5-21 zone transitions, the
/// K5-20 frames and the entity replication, and clients with entity and construct mirrors and client frames over an in-memory
/// network with latency, jitter and losses of the unreliable channel. Players are placed by the test: the replication reads their
/// frame, sequence, seat and local pose from the world state (a seated player does not move in its seat).
/// </summary>
internal sealed class ShipCoopRig : IAsyncDisposable
{
    /// <summary>Render delay of the clients in ticks (200 ms: one-way latency, jitter and up to two lost snapshots).</summary>
    public const int RenderDelayTicks = 12;

    private readonly Dictionary<PlayerId, bool> _standing = [];

    private ShipCoopRig(ServerRig rig, FakeCharacters characters, EntityReplicationBinder binder, ServerFramesBinder frames, ShipFlightHost flight, ServerZoneTransitions zones, StarSystemModel model)
    {
        Rig = rig;
        Characters = characters;
        Binder = binder;
        Frames = frames;
        FlightHost = flight;
        Zones = zones;
        Model = model;
    }

    public ServerRig Rig { get; }

    public FakeCharacters Characters { get; }

    public EntityReplicationBinder Binder { get; }

    public ServerFramesBinder Frames { get; }

    public ShipFlightHost FlightHost { get; }

    public ServerZoneTransitions Zones { get; }

    public StarSystemModel Model { get; }

    public ServerRuntime Runtime => Rig.Runtime;

    public EntityReplication Replication => Binder.Replication ?? throw new InvalidOperationException("No replication yet.");

    public ShipFlightSystem Flight => FlightHost.Flight ?? throw new InvalidOperationException("No flight yet.");

    public DevControlSource Pilots => FlightHost.Dev ?? throw new InvalidOperationException("No development pilots.");

    public IFrameHistoryView ServerHistory => Frames.History?.History ?? throw new InvalidOperationException("No frames yet.");

    public long ServerTick => (long)Runtime.State.CurrentTick.Value;

    public List<ShipCoopClient> Clients { get; } = [];

    /// <summary>
    /// Starts a server over the star system of <paramref name="seed"/> with the ship block content. <paramref name="lossy"/> uses
    /// RTT 100 ms ± 10 ms and 5 % losses of the unreliable channel (X11: the reliable channel is lossless).
    /// </summary>
    public static async Task<ShipCoopRig> StartAsync(int seed, bool lossy = true)
    {
        var created = StarSystemGenesis.CreateWorldBodies((ulong)seed, WorldFactory.HomePlanetParams.GeneratorProfile);
        var model = new StarSystemModel(created.System, created.Bodies);
        var conditions = lossy ? new LinkConditions(TimeSpan.FromMilliseconds(45), TimeSpan.FromMilliseconds(10), 0.05, 0, seed) : null;
        var rig = new ServerRig(
            createWorld: () => WorldState.CreateNew(WorldFactory.DefaultWorldId, (ulong)seed, created.System, created.Bodies, ShipFixtures.BlockKeys, ["stone", "dirt"]),
            conditions: conditions,
            configure: options => options with { BlockTypeKeys = ShipFixtures.BlockKeys, BlockDefinitions = ShipFixtures.Definitions });
        var characters = new FakeCharacters();
        var frames = new ServerFramesBinder();
        frames.Register(rig.Runtime);
        var flight = new ShipFlightHost(
            runtime => new BodyCollider(runtime, model), _ => new ReadyGate(), dev: true, 60, rig.Log.CreateLogger<ShipFlightHost>());
        rig.Runtime.AddTickParticipant(flight);
        rig.Runtime.AddTickParticipant(new ShipStatusSender(() => flight.Flight));
        var zones = new ServerZoneTransitions(
            () => frames.EnsureWorld(rig.Runtime) is not null ? frames.Transitions : null, 60, rig.Log.CreateLogger<ServerZoneTransitions>());
        ServerZoneTransitions.RegisterFramesPhase(rig.Runtime, zones, null);
        var shipRig = default(ShipCoopRig);
        rig.Runtime.AddTickParticipant(new CharacterSync(() => shipRig));
        var binder = new EntityReplicationBinder(runtime => new ServerReplicationSource(runtime.State, characters), rig.Time, rig.Log);
        binder.Register(rig.Runtime);
        await rig.StartAsync();
        shipRig = new ShipCoopRig(rig, characters, binder, frames, flight, zones, model);
        return shipRig;
    }

    /// <summary>The surface direction of the home planet toward a body (frames share axes in the early access).</summary>
    public Vector3D UpToward(int body)
    {
        var home = Model.GetBody(0);
        return Vector3D.Normalize(Model.GetBody(body).Position - home.Position);
    }

    /// <summary>
    /// Builds the 30-block ship standing on the home planet with its up axis along <paramref name="up"/>, switches it to ship mode
    /// and fills its tanks.
    /// </summary>
    public EntityId AddShip(Vector3D up)
    {
        var stats = FlightFixtures.Thirty();
        var radius = Model.GetBody(0).RadiusMeters;
        var rotation = FromTo(new Vector3D(0, 1, 0), up);
        var com = Vector3D.FromFloat(stats.CenterOfMass);
        var origin = (up * (radius + stats.BoundingRadius + 0.01)) - rotation.Rotate(com);
        var id = Runtime.State.EntityIds.PeekNext();
        var q = Quaternion.Normalize(new Quaternion((float)rotation.X, (float)rotation.Y, (float)rotation.Z, (float)rotation.W));
        foreach (var op in ShipFixtures.BuildOperations(id, ShipFixtures.Thirty, new Vector3((float)origin.X, (float)origin.Y, (float)origin.Z)))
        {
            Runtime.Simulation.ApplyServerOperation(op is ConstructCreateOp create ? create with { Parent = FrameRef.Of(Runtime.State.HomePlanet), Rotation = q } : op);
        }

        SetMode(id, ConstructMode.Ship);
        Runtime.Simulation.ApplyServerOperation(new ShipFuelAddOp(id, stats.FuelCapacity, stats.FuelCapacity));
        return id;
    }

    public Construct Ship(EntityId id) =>
        Runtime.State.Constructs.TryGet(id, out var construct) ? construct : throw new InvalidOperationException("Unknown construct.");

    public void SetMode(EntityId ship, ConstructMode mode) => Runtime.Simulation.ApplyServerOperation(TestShips.ModeOp(Ship(ship), mode));

    public async Task<ShipCoopClient> JoinAsync(string name, ulong identity)
    {
        var client = new ShipCoopClient(await Rig.JoinAsync(name, identity), Rig.Time, Runtime.State);
        Clients.Add(client);
        return client;
    }

    public EntityId EntityOf(PlayerId player) =>
        Runtime.State.Players.TryGet(player, out var state) ? state.Entity : throw new InvalidOperationException("Unknown player.");

    public PlayerState PlayerOf(PlayerId player) =>
        Runtime.State.Players.TryGet(player, out var state) ? state : throw new InvalidOperationException("Unknown player.");

    /// <summary>Stands a player on the home planet at <paramref name="feet"/> (planet frame).</summary>
    public void Stand(PlayerId player, Vector3 feet)
    {
        var state = PlayerOf(player);
        if (state.Frame.Kind != FrameKind.Planet)
        {
            Reparent(player, FrameRef.Of(Runtime.State.HomePlanet), null);
        }

        state = PlayerOf(player);
        Assert.True(Runtime.Simulation.UpdatePlayerKinematics(player, state.Frame, state.ParentSeq, feet, Quaternion.Identity, 0));
        _standing[player] = true;
    }

    /// <summary>Seats a player in a cell of a ship: moves it to the cell centre in the ship's parent frame, then reparents.</summary>
    public void Seat(PlayerId player, EntityId ship, CellPos cell)
    {
        var construct = Ship(ship);
        var state = PlayerOf(player);
        if (state.Frame != FrameRef.Of(ship))
        {
            if (state.Frame != construct.Parent)
            {
                Reparent(player, construct.Parent, null);
                state = PlayerOf(player);
            }

            Assert.True(Runtime.Simulation.UpdatePlayerKinematics(
                player, state.Frame, state.ParentSeq, construct.CellCenterToParent(cell), construct.Rotation, 0));
        }
        else
        {
            // Seat to seat inside the ship: move in the ship frame first.
            Assert.True(Runtime.Simulation.UpdatePlayerKinematics(
                player, state.Frame, state.ParentSeq, new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f), Quaternion.Identity, 0));
        }

        Reparent(player, FrameRef.Of(ship), cell);
        _standing[player] = false;
    }

    public void Fly(EntityId ship, int body) => Assert.True(Pilots.Start(ship, Model.GetBody(body).Planet));

    public bool IsFlying(EntityId ship) => Pilots.TryGetPhase(ship, out _);

    /// <summary>Flight state of a ship for failure messages.</summary>
    public string Describe(EntityId ship)
    {
        var construct = Ship(ship);
        var phase = Pilots.TryGetPhase(ship, out var p) ? p.ToString() : "no pilot";
        var status = Flight.TryGetStatus(ship, out var s) ? s.ToString() : "-";
        return $"ship {ship.Value} {phase} in {construct.Parent.Kind}:{construct.Parent.Id} at {construct.Origin} seq {construct.ParentSeq} {status}";
    }

    public bool IsParked(EntityId ship) =>
        Flight.TryGetStatus(ship, out var status) && (status.Flags & ShipFlightFlags.Parked) != 0;

    /// <summary>Whether any construct transfer to any client is in progress.</summary>
    public bool AnyTransfer()
    {
        foreach (var client in Clients)
        {
            foreach (var construct in Runtime.State.Constructs.All)
            {
                if (Replication.Constructs.IsTransferring(client.Player, construct.Id))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>One frame: the server ticks, the clients poll; <paramref name="measure"/> runs after the clients.</summary>
    public void Step(Action? measure = null)
    {
        Rig.Step();
        measure?.Invoke();
    }

    public void Steps(int frames, Action? measure = null)
    {
        for (var i = 0; i < frames; i++)
        {
            Step(measure);
        }
    }

    /// <summary>Steps until the condition holds; false after <paramref name="maxFrames"/>.</summary>
    public bool StepUntil(Func<bool> condition, int maxFrames, Action? measure = null)
    {
        for (var i = 0; i < maxFrames; i++)
        {
            if (condition())
            {
                return true;
            }

            Step(measure);
        }

        return condition();
    }

    /// <summary>Waits for parked ships without pilots and without transfers, then one second of quiet.</summary>
    public void Settle(params EntityId[] ships)
    {
        Assert.True(
            StepUntil(() => ships.All(s => !IsFlying(s) && IsParked(s)) && !AnyTransfer(), 60 * 60),
            "ships did not park or transfers did not end");
        Steps(60);
        Assert.False(AnyTransfer());
    }

    public ValueTask DisposeAsync() => Rig.DisposeAsync();

    private void Reparent(PlayerId player, FrameRef frame, CellPos? seat)
    {
        Assert.NotNull(Frames.Transitions);
        Assert.True(Frames.Transitions.TryReparent(EntityOf(player), frame, seat, out _, out var failure), failure.ToString());
    }

    private void SyncCharacters()
    {
        foreach (var player in Runtime.State.Players.All)
        {
            if (!player.IsOnline)
            {
                continue;
            }

            Characters.States[player.Player] = new ServerCharacterState(
                Runtime.State.HomePlanet, player.Frame, player.ParentSeq, player.Position, player.BodyRotation, 0, Vector3.Zero,
                MovementMode.Walking, 0);
        }
    }

    private static QuaternionD FromTo(in Vector3D a, in Vector3D b)
    {
        var axis = Vector3D.Cross(a, b);
        var length = axis.Length();
        var cosine = Math.Clamp(Vector3D.Dot(a, b), -1, 1);
        return length < 1e-12 ? QuaternionD.Identity : QuaternionD.FromAxisAngle(axis / length, Math.Acos(cosine));
    }

    /// <summary>Copies the world state of the players into the character source before the replication reads it.</summary>
    private sealed class CharacterSync(Func<ShipCoopRig?> rig) : IServerTickParticipant
    {
        public void OnTick(ServerRuntime runtime) => rig()?.SyncCharacters();
    }

    /// <summary>Every body a ball of its radius, placed in the parent frame of the ship (planet frames relative to their body).</summary>
    private sealed class BodyCollider(ServerRuntime runtime, StarSystemModel model) : IShipCollider
    {
        private readonly Dictionary<FrameRef, SphereGroundCollider> _grounds = [];

        public ShipSweepResult Sweep(EntityId ship, in PoseD startPose, in PoseD endPose)
        {
            if (!runtime.State.Constructs.TryGet(ship, out var construct) || !runtime.ShipStats.TryGet(ship, out var stats))
            {
                return ShipSweepResult.Free;
            }

            return Ground(construct.Parent).Sweep(startPose, endPose, stats);
        }

        public void Forget(EntityId ship)
        {
        }

        private SphereGroundCollider Ground(FrameRef frame)
        {
            if (!_grounds.TryGetValue(frame, out var ground))
            {
                ground = new SphereGroundCollider();
                var origin = Vector3D.Zero;
                if (frame.Kind == FrameKind.Planet && model.TryGetBody(new PlanetId(frame.Id), out var parent))
                {
                    origin = parent.Position;
                }

                for (var i = 0; i < model.BodyCount; i++)
                {
                    ground.Add(model.GetBody(i).Position - origin, model.GetBody(i).RadiusMeters);
                }

                _grounds.Add(frame, ground);
            }

            return ground;
        }
    }

    /// <summary>Every terrain ready (the harness has no terrain streaming).</summary>
    private sealed class ReadyGate : IShipTerrainGate
    {
        public ShipTerrainGate Query(EntityId ship, PlanetId planet, in ShipMotion motion, ShipStats stats, out double horizontalSpeedCap)
        {
            horizontalSpeedCap = double.PositiveInfinity;
            return ShipTerrainGate.Ready;
        }

        public ShipTerrainGate QueryGround(EntityId ship, PlanetId planet, in ShipMotion motion, ShipStats stats, double depthMeters) =>
            ShipTerrainGate.Ready;

        public void Forget(EntityId ship)
        {
        }
    }
}

/// <summary>A client of the ship harness: entity and construct mirrors, client frames, resync counts.</summary>
internal sealed class ShipCoopClient
{
    public ShipCoopClient(RigClient client, TimeProvider time, WorldState world)
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

        // Planet placement is static data of the world (the client learns it from StarSystemState 32; the harness reads it directly).
        Frames = new ClientFrames(Replication.Entities, Replication.Constructs, new WorldStatePlanetPlacement(world));
        Frames.OnServerHello(client.Hello!);
        Replication.Decoder.AddSink(Frames);
        Replication.Decoder.AddSink(SnapshotTicks);
        SnapshotTicks.IsShip = id => Constructs.TryGetConstruct(id, out var construct) && construct.Mode == ConstructMode.Ship;
        client.Observer = Replication.OnMessage;
    }

    /// <summary>Gets the ticks of the full snapshots that carried each entity (the samples the client interpolates between).</summary>
    public EntitySnapshotTicks SnapshotTicks { get; } = new();

    public RigClient Client { get; }

    public PlayerId Player => Client.Player;

    public ClientReplication Replication { get; }

    public ClientFrames Frames { get; }

    public EntityMirror Entities => Replication.Entities;

    public ConstructMirror Constructs => Replication.Constructs!;

    public int Resyncs { get; private set; }
}

/// <summary>Ticks of the full snapshots a client applied, per entity they carried.</summary>
internal sealed class EntitySnapshotTicks : IFullSnapshotSink
{
    private readonly Dictionary<EntityId, List<long>> _ticks = [];

    /// <summary>Gets or sets the client's own entity, for <see cref="OwnShipMissing"/>.</summary>
    public EntityId Own { get; set; }

    /// <summary>Gets or sets whether a construct is a ship-mode construct on the client (static ones are not in snapshots).</summary>
    public Func<EntityId, bool> IsShip { get; set; } = _ => true;

    /// <summary>Gets the full snapshots that placed the own entity in a construct frame without that construct (expected 0).</summary>
    public int OwnShipMissing { get; private set; }

    /// <summary>Gets the full snapshots that placed the own entity in a construct frame.</summary>
    public int InShip { get; private set; }

    public void OnFullSnapshot(Snapshot full)
    {
        foreach (var entity in full.Entities)
        {
            if (entity.Entity == Own && entity.FrameIndex < full.Frames.Count && full.Frames[entity.FrameIndex].Frame.Kind == FrameKind.Construct)
            {
                var ship = new EntityId(full.Frames[entity.FrameIndex].Frame.Id);
                if (IsShip(ship))
                {
                    InShip++;
                    if (!full.Entities.Any(e => e.Entity == ship))
                    {
                        OwnShipMissing++;
                    }
                }
            }

            if (!_ticks.TryGetValue(entity.Entity, out var ticks))
            {
                _ticks[entity.Entity] = ticks = [];
            }

            var tick = (long)full.ServerTick.Value;
            if (ticks.Count == 0 || ticks[^1] < tick)
            {
                ticks.Add(tick);
            }
        }
    }

    /// <summary>The snapshot ticks of an entity around <paramref name="tick"/>: the last at or before it, the first after it.</summary>
    /// <returns>False without a sample at or before the tick; <paramref name="after"/> is null while the client extrapolates.</returns>
    public bool TryGetBracket(EntityId entity, long tick, out long before, out long? after)
    {
        before = 0;
        after = null;
        if (!_ticks.TryGetValue(entity, out var ticks))
        {
            return false;
        }

        var at = ticks.BinarySearch(tick);
        var index = at >= 0 ? at : ~at - 1;
        if (index < 0)
        {
            return false;
        }

        before = ticks[index];
        if (index + 1 < ticks.Count)
        {
            after = ticks[index + 1];
        }

        return true;
    }
}
