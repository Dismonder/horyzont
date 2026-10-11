using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
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

public sealed class EntityReplicationLifecycleTests
{
    [Fact]
    public async Task Replicate_HostLocal_UsesOneConstructWorldAndTerrain()
    {
        await using var rig = await ReplicationRig.StartAsync();
        // A client named "local" connects from the runtime's local endpoint: the host's own client shares the world.
        var host = await rig.JoinAsync("local", 1);
        var remote = await rig.JoinAsync("remote", 2, new Vector3(20, 4000, 0));
        Assert.True(rig.Runtime.IsLocalPlayer(host.Player));
        var construct = rig.AddConstruct(new Vector3(5, 4000, 5));
        rig.Step(9);
        rig.Runtime.Simulation.ApplyServerOperation(new BlockSetOp(construct, new CellPos(1, 0, 0), ReplicationRig.Stone));
        rig.Step(6);

        Assert.Contains(host.Received, m => m is EntitySpawn s && s.Entity == rig.EntityOf(remote.Player));
        Assert.DoesNotContain(host.Received, m => m is ConstructState or OperationApplied or ConstructRemoved);
        Assert.DoesNotContain(host.Received, m => m is EntitySpawn { Kind: EntityKind.Construct });
        Assert.Contains(remote.Received, m => m is ConstructState);
        Assert.Contains(remote.Received, m => m is OperationApplied);
        Assert.Contains(host.Received, m => m is Snapshot);
    }

    [Fact]
    public async Task Shutdown_ReplicationActive_DrainsAndReleasesOnce()
    {
        var budget = new GatedSendBudget { Open = false };
        await using var rig = await ReplicationRig.StartAsync(sendBudget: budget);
        var alice = await rig.JoinAsync("alice", 1);
        var construct = rig.AddConstruct(new Vector3(5, 4000, 5), new CellPos(40, 0, 0));
        rig.Step(9);
        rig.Runtime.Simulation.ApplyServerOperation(new BlockSetOp(construct, new CellPos(1, 0, 0), ReplicationRig.Stone));
        rig.Step(3);
        var replication = rig.Replication;
        Assert.True(replication.Constructs.IsTransferring(alice.Player, construct));
        Assert.True(replication.Constructs.StagedBytes(alice.Player) > 0);

        await rig.Rig.StopAsync();
        Assert.Equal(0, replication.TrackedPlayers);
        Assert.Equal(0, replication.Constructs.StagedBytes(alice.Player));
        Assert.Equal(0, replication.Constructs.BufferedOperations(alice.Player));
        var received = alice.Received.Count;

        // Detaching again and ticking a released replication neither throws nor sends.
        replication.Detach();
        replication.Step(new Tick(300));
        Assert.Equal(received, alice.Received.Count);
    }

    [Fact]
    public async Task Replicate_PlayerLeaves_OthersGetOneDespawnAndNoStateLeaks()
    {
        await using var rig = await ReplicationRig.StartAsync();
        var alice = await rig.JoinAsync("alice", 1);
        var bob = await rig.JoinAsync("bob", 2, new Vector3(20, 4000, 0));
        rig.Step(9);
        var bobBody = rig.EntityOf(bob.Player);
        Assert.True(rig.Replication.Knows(alice.Player, bobBody));
        bob.Session.Disconnect();
        rig.Step(120);
        Assert.Equal(1, alice.Received.OfType<EntityDespawn>().Count(m => m.Entity == bobBody));
        Assert.False(rig.Replication.Knows(alice.Player, bobBody));
        Assert.Equal(1, rig.Replication.TrackedPlayers);
    }

    [Fact]
    public async Task Replicate_PlayerLeaves_BudgetsForgetThePlayer()
    {
        var send = new GatedSendBudget();
        var snapshots = new RecordingSnapshotBudget();
        await using var rig = await ReplicationRig.StartAsync(snapshotBudget: snapshots, sendBudget: send);
        var bob = await rig.JoinAsync("bob", 2);
        rig.Step(9);
        bob.Session.Disconnect();
        rig.Step(120);
        Assert.Equal([bob.Player], send.Forgotten);
        Assert.Equal([bob.Player], snapshots.Forgotten);
    }

    [Fact]
    public void Step_SteadyStateEightPlayers_AllocatesNothing()
    {
        using var transport = new CopyingTransport();
        var simulation = new ServerSimulation(WorldFactory.CreateDefault(), new FakeTerrainService(), new InMemoryJournal(),
            new GameRules(), NullLogger<ServerSimulation>.Instance);
        var time = new FakeTimeProvider();
        var session = new ServerSession(transport, new ServerOptions { BuildHash = 1 },
            new GameServerHooks(simulation, () => true, NullLogger<GameServerHooks>.Instance), time);
        session.Poll(new Tick(1));
        Assert.Equal(8, session.PlayersInGame.Count);
        var characters = new FakeCharacters();
        var source = new ServerReplicationSource(simulation.State, characters);
        var replication = new EntityReplication(session, simulation, source, new FixedSnapshotBudget(), new TokenBucketReplicationBudget(time),
            time, NullLogger<EntityReplication>.Instance);
        var players = session.PlayersInGame;

        // K5-20: a moving ship in the snapshots and a player in its frame (interest through the frame graph).
        var ship = simulation.State.EntityIds.PeekNext();
        simulation.ApplyServerOperation(new ConstructCreateOp(
            ship, FrameRef.Of(WorldFactory.HomePlanet), new Vector3(0, 4010, 0), Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(1, 0)));
        Assert.True(simulation.State.Constructs.TryGet(ship, out var shipConstruct));
        simulation.ApplyServerOperation(TestShips.ModeOp(shipConstruct!, ConstructMode.Ship));
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
            Assert.True(simulation.UpdateConstructKinematics(ship, FrameRef.Of(WorldFactory.HomePlanet), 0, new Vector3(x, 4010, 0), Quaternion.Identity));
            for (var i = 0; i < players.Count; i++)
            {
                characters.States[players[i]] = i == 0
                    ? new ServerCharacterState(
                        WorldFactory.HomePlanet, FrameRef.Of(ship), 0, new Vector3(0.5f, 1, 0.5f), Quaternion.Identity, 0, Vector3.Zero,
                        MovementMode.Walking, 0)
                    : new ServerCharacterState(
                        WorldFactory.HomePlanet, FrameRef.Of(WorldFactory.HomePlanet), 0, new Vector3(x + i, 4000, 0), Quaternion.Identity, 0,
                        Vector3.UnitX, MovementMode.Walking, 0);
            }

            replication.Step(new Tick(tick));

            // The client acknowledges what it reconstructed, so the builder works from a baseline and not only from full states.
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
        for (ulong tick = 0; tick < 600; tick++)
        {
            Round(tick);
        }

        transport.Inspect = false;
        Assert.True(transport.DeltasSeen > 100, "the warm-up must reach the baseline branch: " + transport.DeltasSeen);
        var sent = transport.SnapshotsSent;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong tick = 600; tick < 10_600; tick++)
        {
            Round(tick);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.InRange(transport.SnapshotsSent - sent, (8 * (10_000 / 3)) - 16, (8 * (10_000 / 3)) + 16);
    }
}
