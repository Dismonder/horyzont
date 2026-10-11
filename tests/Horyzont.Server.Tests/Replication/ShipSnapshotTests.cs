using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Server.Characters;
using Horyzont.Game.Server.Replication;
using Horyzont.Net.Session;
using Horyzont.Server.Replication;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests.Replication;

/// <summary>K5-26 decisions 4 and 6: snapshots only for live chains, the recipient's own chain in every snapshot.</summary>
public sealed class ShipSnapshotTests
{
    private static readonly Vector3 _here = new(1_000_000f, 0f, 0f);

    private static bool InAnySnapshot(RigClient client, EntityId entity) =>
        client.Received.OfType<Snapshot>().Any(s => s.Entities.Any(e => e.Entity == entity));

    private static bool Mentions(Message message, EntityId entity) => message switch
    {
        EntitySpawn s => s.Entity == entity,
        EntityDespawn d => d.Entity == entity,
        ConstructState c => c.Header.Id == entity,
        ConstructRemoved r => r.Construct == entity,
        OperationApplied o => o.Operation is EntityReparentOp op ? op.Entity == entity : o.ConstructChanges.Any(c => c.Construct == entity),
        Snapshot s => s.Entities.Any(e => e.Entity == entity),
        _ => false,
    };

    private static FrameRef SystemFrame(WorldState state)
    {
        Assert.True(state.Planets.TryGet(state.HomePlanet, out var home));
        return FrameRef.Of(home!.System);
    }

    [Fact]
    public async Task Snapshot_ShipNotLive_NotSent_ThenSentAfterConstructState()
    {
        var gate = new GatedSendBudget { Open = false };
        await using var rig = await ReplicationRig.StartAsync(sendBudget: gate);
        var alice = await rig.JoinAsync("alice", 1);
        var bob = await rig.JoinAsync("bob", 2);
        var ship = rig.AddShip(new Vector3(0, 4010, 0));
        rig.PlaceIn(bob.Player, FrameRef.Of(ship), new Vector3(0.5f, 1, 0.5f));
        var bobBody = rig.EntityOf(bob.Player);
        rig.Step(30);

        // Spawned (control traffic), but no snapshots of the ship or of anyone in it before its complete state.
        Assert.True(rig.Replication.Knows(alice.Player, ship));
        Assert.True(rig.Replication.Knows(alice.Player, bobBody));
        Assert.True(rig.Replication.Constructs.IsTransferring(alice.Player, ship));
        Assert.False(InAnySnapshot(alice, ship));
        Assert.False(InAnySnapshot(alice, bobBody));
        Assert.False(InAnySnapshot(bob, bobBody));

        gate.Open = true;
        Assert.True(await rig.Rig.RunUntilAsync(() => rig.Replication.Constructs.IsLive(alice.Player, ship), 600));
        var lastPart = alice.Received.FindLastIndex(m => m is ConstructState c && c.Header.Id == ship);
        rig.Step(9);
        var firstSnapshot = alice.Received.FindIndex(m => m is Snapshot s && s.Entities.Any(e => e.Entity == ship));
        Assert.True(firstSnapshot > lastPart, "a snapshot of the ship before its complete state");
        Assert.True(InAnySnapshot(alice, bobBody));
        Assert.True(InAnySnapshot(bob, bobBody));
    }

    [Fact]
    public async Task ShipOutOfRange_NoTraffic()
    {
        await using var rig = await ReplicationRig.StartAsync(() => WorldFactory.CreateWithStarSystem(0));
        var system = SystemFrame(rig.Runtime.State);
        var alice = await rig.JoinAsync("alice", 1);
        var bob = await rig.JoinAsync("bob", 2);
        var own = rig.AddShipIn(system, _here);
        var far = rig.AddShipIn(system, _here + new Vector3(50_000f, 0, 0));
        rig.PlaceIn(alice.Player, FrameRef.Of(own), Vector3.Zero);
        rig.PlaceIn(bob.Player, FrameRef.Of(far), Vector3.Zero);
        rig.Step(9);
        alice.Received.Clear();
        var bobBody = rig.EntityOf(bob.Player);

        // The far ship flies about and changes frames: none of it reaches Alice.
        for (var i = 0; i < 300; i++)
        {
            rig.MoveShip(far, system, _here + new Vector3(50_000f + i, 0, 0));
            rig.Step();
        }

        rig.MoveShip(far, FrameRef.Of(rig.Runtime.State.HomePlanet), new Vector3(0, 9_000f, 0));
        rig.Step(30);
        Assert.DoesNotContain(alice.Received, m => Mentions(m, far) || Mentions(m, bobBody));
        Assert.True(alice.Received.OfType<Snapshot>().Count() > 50);
    }

    [Fact]
    public async Task Snapshot_OwnShipEverySnapshot_With22Others()
    {
        await using var rig = await ReplicationRig.StartAsync(() => WorldFactory.CreateWithStarSystem(0));
        var system = SystemFrame(rig.Runtime.State);
        var alice = await rig.JoinAsync("alice", 1);
        for (var i = 0; i < 30; i++)
        {
            rig.AddShipIn(system, _here + new Vector3(10f + i, 0, 0));
        }

        // Created last: the highest id loses every tie of the age rule.
        var own = rig.AddShipIn(system, _here);

        rig.PlaceIn(alice.Player, FrameRef.Of(own), Vector3.Zero);
        Assert.True(await rig.Rig.RunUntilAsync(() => rig.Runtime.State.Constructs.All.All(c => rig.Replication.Constructs.IsLive(alice.Player, c.Id)), 3000));
        alice.Received.Clear();
        rig.Step(300);

        var snapshots = alice.Received.OfType<Snapshot>().ToList();
        Assert.True(snapshots.Count >= 90);
        var aliceBody = rig.EntityOf(alice.Player);
        Assert.All(snapshots, s =>
        {
            Assert.Contains(s.Entities, e => e.Entity == aliceBody);
            Assert.Contains(s.Entities, e => e.Entity == own);
        });
    }

    [Fact]
    public async Task Snapshot_FrameTableFull_ChainFramesReserved()
    {
        await using var rig = await ReplicationRig.StartAsync(() => WorldFactory.CreateWithStarSystem(0));
        var system = SystemFrame(rig.Runtime.State);
        var alice = await rig.JoinAsync("alice", 1);

        // Seven more ships with a passenger each (eight players at most): eight ship frames and the system frame do not fit a table
        // of eight.
        var passengers = new List<EntityId>();
        for (var i = 0; i < 7; i++)
        {
            var ship = rig.AddShipIn(system, _here + new Vector3(20f * (i + 1), 0, 0));
            var client = await rig.JoinAsync("p" + i, (ulong)(10 + i));
            rig.PlaceIn(client.Player, FrameRef.Of(ship), Vector3.Zero);
            passengers.Add(rig.EntityOf(client.Player));
        }

        // Created last: the highest id loses every tie of the age rule.
        var own = rig.AddShipIn(system, _here);
        rig.PlaceIn(alice.Player, FrameRef.Of(own), Vector3.Zero);

        Assert.True(await rig.Rig.RunUntilAsync(() => rig.Runtime.State.Constructs.All.All(c => rig.Replication.Constructs.IsLive(alice.Player, c.Id)), 3000));
        alice.Received.Clear();
        rig.Step(180);

        var snapshots = alice.Received.OfType<Snapshot>().ToList();
        var aliceBody = rig.EntityOf(alice.Player);
        Assert.All(snapshots, s =>
        {
            Assert.True(s.Frames.Count <= 8);
            Assert.Contains(s.Entities, e => e.Entity == aliceBody);
            Assert.Contains(s.Entities, e => e.Entity == own);
        });

        // The rest rotates by age: nobody starves.
        Assert.All(passengers, p => Assert.Contains(snapshots, s => s.Entities.Any(e => e.Entity == p)));
    }

    [Fact]
    public void ShipReplication_EightShipsEightPlayers_RoundAllocatesZeroBytes()
    {
        using var transport = new CopyingTransport();
        var world = WorldFactory.CreateWithStarSystem(0);
        var simulation = new ServerSimulation(world, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        var time = new FakeTimeProvider();
        var session = new ServerSession(transport, new ServerOptions { BuildHash = 1 },
            new GameServerHooks(simulation, () => true, NullLogger<GameServerHooks>.Instance), time);
        session.Poll(new Tick(1));
        Assert.Equal(8, session.PlayersInGame.Count);
        var characters = new FakeCharacters();
        var replication = new EntityReplication(session, simulation, new ServerReplicationSource(simulation.State, characters),
            new FixedSnapshotBudget(), new TokenBucketReplicationBudget(time), time, NullLogger<EntityReplication>.Instance, ReplicationRanges.Default);
        var players = session.PlayersInGame;
        var system = SystemFrame(simulation.State);
        var home = FrameRef.Of(simulation.State.HomePlanet);
        var ships = new EntityId[8];
        for (var i = 0; i < ships.Length; i++)
        {
            var id = simulation.State.EntityIds.PeekNext();
            simulation.ApplyServerOperation(new ConstructCreateOp(id, home, new Vector3(0, 4010, 0), Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(1, 0)));
            Assert.True(simulation.State.Constructs.TryGet(id, out var construct));
            simulation.ApplyServerOperation(TestShips.ModeOp(construct!, ConstructMode.Ship));
            simulation.ApplyServerOperation(new EntityReparentOp(id, system, 1, _here + new Vector3(100f * i, 0, 0), Quaternion.Identity, Vector3.Zero, null));
            ships[i] = id;
        }

        var acks = new SnapshotAck[players.Count][];
        for (var i = 0; i < acks.Length; i++)
        {
            acks[i] = new SnapshotAck[4000];
            for (var id = 0; id < acks[i].Length; id++)
            {
                acks[i][id] = new SnapshotAck((uint)id);
            }
        }

        var x = 0f;

        void Round(ulong tick)
        {
            x += 0.001f;
            for (var i = 0; i < ships.Length; i++)
            {
                Assert.True(simulation.UpdateConstructKinematics(ships[i], system, 1, _here + new Vector3((100f * i) + x, 0, 0), Quaternion.Identity));
                characters.States[players[i]] = new ServerCharacterState(
                    simulation.State.HomePlanet, FrameRef.Of(ships[i]), 0, new Vector3(0.5f, 1, 0.5f), Quaternion.Identity, 0, Vector3.Zero,
                    MovementMode.Walking, 0);
            }

            replication.Step(new Tick(tick));
            for (var i = 0; i < players.Count; i++)
            {
                var sentTo = transport.SentTo((ulong)players[i].Value);
                if (sentTo > 0 && sentTo < acks[i].Length)
                {
                    replication.Handle(players[i], acks[i][sentTo]);
                }
            }
        }

        transport.Inspect = true;
        for (ulong tick = 0; tick < 900; tick++)
        {
            Round(tick);
        }

        transport.Inspect = false;
        Assert.All(ships, s => Assert.All(players, p => Assert.True(replication.Constructs.IsLive(p, s))));
        Assert.True(transport.DeltasSeen > 100, "the warm-up must reach the baseline branch: " + transport.DeltasSeen);
        var sent = transport.SnapshotsSent;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong tick = 900; tick < 3_900; tick++)
        {
            Round(tick);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.InRange(transport.SnapshotsSent - sent, (8 * 1_000) - 16, (8 * 1_000) + 16);
    }
}
