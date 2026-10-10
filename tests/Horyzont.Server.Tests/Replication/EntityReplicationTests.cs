using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Replication;

public sealed class EntityReplicationTests
{
    private static readonly PlanetId _second = new(2);

    private static WorldState TwoPlanets() =>
        WorldFactory.CreateWith(WorldFactory.HomePlanetParams, new PlanetParams(_second, new SystemId(1), 9, "core:test/1"));

    private static int Spawns(RigClient client, EntityId entity) =>
        client.Received.OfType<EntitySpawn>().Count(m => m.Entity == entity);

    private static int Despawns(RigClient client, EntityId entity) =>
        client.Received.OfType<EntityDespawn>().Count(m => m.Entity == entity);

    [Fact]
    public async Task Interest_EnterLeaveAndOtherPlanet_OnlyExpectedEvents()
    {
        await using var rig = await ReplicationRig.StartAsync(TwoPlanets);
        var alice = await rig.JoinAsync("alice", 1);
        var bob = await rig.JoinAsync("bob", 2, new Vector3(499, 4000, 0));
        var carol = await rig.JoinAsync("carol", 3, new Vector3(0, 4000, 0), _second);
        var big = rig.AddConstruct(new Vector3(800, 4000, 0), new CellPos(300, 0, 0));
        rig.Step(9);

        var aliceBody = rig.EntityOf(alice.Player);
        var bobBody = rig.EntityOf(bob.Player);
        var carolBody = rig.EntityOf(carol.Player);
        Assert.Equal(1, Spawns(alice, aliceBody));
        Assert.Equal(1, Spawns(alice, bobBody));
        Assert.Equal(0, Spawns(alice, carolBody));
        Assert.Equal(1, Spawns(carol, carolBody));
        Assert.Equal(0, Spawns(carol, aliceBody));

        // The construct origin is 800 m away, but its bounding sphere (radius > 300 m) reaches into the 500 m range.
        Assert.Equal(1, Spawns(alice, big));
        Assert.Equal(1, Spawns(bob, big));
        Assert.Equal(0, Spawns(carol, big));

        // 501 m: still inside the hysteresis band (leaves above 550 m).
        rig.Place(bob.Player, new Vector3(501, 4000, 0));
        rig.Step(9);
        Assert.Equal(0, Despawns(alice, bobBody));
        rig.Place(bob.Player, new Vector3(551, 4000, 0));
        rig.Step(9);
        Assert.Equal(1, Despawns(alice, bobBody));
        rig.Step(30);
        Assert.Equal(1, Despawns(alice, bobBody));
        Assert.Equal(1, Spawns(alice, bobBody));

        rig.Place(bob.Player, new Vector3(499, 4000, 0));
        rig.Step(9);
        Assert.Equal(2, Spawns(alice, bobBody));
        Assert.Equal(1, Despawns(alice, bobBody));
        Assert.Equal(1, Spawns(alice, aliceBody));
        Assert.Equal(0, Despawns(alice, aliceBody));
    }

    [Fact]
    public async Task Snapshot_OwnEntityFirstEveryThirdTick_NoSnapshotWithoutBody()
    {
        await using var rig = await ReplicationRig.StartAsync();
        var alice = await rig.JoinAsync("alice", 1);
        var bob = await rig.JoinAsync("bob", 2, new Vector3(10, 4000, 0));
        rig.Step(60);
        var aliceBody = rig.EntityOf(alice.Player);
        var snapshots = alice.Received.OfType<Snapshot>().ToList();
        Assert.InRange(snapshots.Count, 15, 25);
        Assert.All(snapshots, s => Assert.Equal(aliceBody, s.Entities[0].Entity));
        Assert.Contains(snapshots, s => s.Entities.Any(e => e.Entity == rig.EntityOf(bob.Player)));
        for (var i = 1; i < snapshots.Count; i++)
        {
            Assert.Equal(snapshots[i - 1].SnapshotId + 1, snapshots[i].SnapshotId);
        }
    }

    [Fact]
    public async Task Resync_InvisibleEntityAndFlood_DoesNotLeakOrExceedRate()
    {
        await using var rig = await ReplicationRig.StartAsync(TwoPlanets);
        var alice = await rig.JoinAsync("alice", 1);
        var carol = await rig.JoinAsync("carol", 3, new Vector3(0, 4000, 0), _second);
        var mine = rig.AddConstruct(new Vector3(20, 4000, 0));
        var far = rig.AddConstruct(new Vector3(0, 4000, 3000));
        rig.Step(30);
        var states = alice.Received.OfType<ConstructState>().Count(m => m.Header.Id == mine);
        Assert.Equal(1, states);

        alice.Session.Send(new ResyncRequest(far));
        alice.Session.Send(new ResyncRequest(rig.EntityOf(carol.Player)));
        alice.Session.Send(new ResyncRequest(new EntityId(9_999_999)));
        rig.Step(6);
        Assert.DoesNotContain(alice.Received.OfType<ConstructState>(), m => m.Header.Id == far);
        Assert.Equal(0, Spawns(alice, rig.EntityOf(carol.Player)));

        // 100 requests for a visible construct inside one second: at most 4 are honoured (the rest are refused by rate),
        // and requests while a transfer runs are not stacked.
        var before = alice.Received.OfType<ConstructState>().Count(m => m.Header.Id == mine);
        for (var i = 0; i < 100; i++)
        {
            alice.Session.Send(new ResyncRequest(mine));
        }

        rig.Step(30);
        var added = alice.Received.OfType<ConstructState>().Count(m => m.Header.Id == mine) - before;
        Assert.InRange(added, 1, 4);
        Assert.Empty(carol.Received.OfType<ConstructState>());
    }

    [Fact]
    public async Task Operations_VisibleConstructOnly_InOrderAfterState()
    {
        await using var rig = await ReplicationRig.StartAsync();
        var alice = await rig.JoinAsync("alice", 1);
        var bob = await rig.JoinAsync("bob", 2, new Vector3(0, 4000, 2000));
        var near = rig.AddConstruct(new Vector3(10, 4000, 0));
        rig.Step(9);

        rig.Runtime.Simulation.ApplyServerOperation(new BlockSetOp(near, new CellPos(1, 0, 0), ReplicationRig.Stone));
        rig.Runtime.Simulation.ApplyServerOperation(new BlockClearOp(near, new CellPos(1, 0, 0)));
        rig.Step(3);
        var seen = alice.Received.Where(m => m is ConstructState or OperationApplied).ToList();
        Assert.IsType<ConstructState>(seen[0]);
        Assert.Equal(new uint[] { 2, 3 }, seen.OfType<OperationApplied>().Select(o => o.ConstructChanges[0].Version).ToArray());
        Assert.Empty(bob.Received.OfType<OperationApplied>());
        Assert.Empty(bob.Received.OfType<ConstructState>());

        // Removing the last block withdraws the construct explicitly.
        rig.Runtime.Simulation.ApplyServerOperation(new BlockClearOp(near, new CellPos(0, 0, 0)));
        rig.Step(9);
        Assert.Contains(alice.Received, m => m is ConstructRemoved r && r.Construct == near);
        Assert.Equal(0, Despawns(alice, near));
        Assert.Equal(0, rig.Replication.Constructs.StagedBytes(alice.Player));
    }

    [Fact]
    public async Task Apply_MultipartConstructWithEditsDuringTransfer_BuffersOperationsBehindLastPart()
    {
        var budget = new GatedSendBudget { Open = false };
        await using var rig = await ReplicationRig.StartAsync(sendBudget: budget);
        var alice = await rig.JoinAsync("alice", 1, new Vector3(0, 4000, 0));
        var id = rig.Runtime.State.EntityIds.PeekNext();
        rig.Runtime.Simulation.ApplyServerOperation(new ConstructCreateOp(
            id, FrameRef.Of(WorldFactory.HomePlanet), new Vector3(10, 4000, 0), System.Numerics.Quaternion.Identity, new CellPos(0, 0, 0), ReplicationRig.Stone));
        // 40 chunks in a row: several parts of at most 12 chunks.
        for (var c = 1; c < 40; c++)
        {
            rig.Runtime.Simulation.ApplyServerOperation(new BlockSetOp(id, new CellPos(c * 16, 0, 0), ReplicationRig.Stone));
        }

        rig.Step(3);
        Assert.Empty(alice.Received.OfType<ConstructState>());
        rig.Runtime.Simulation.ApplyServerOperation(new BlockSetOp(id, new CellPos(1, 0, 0), ReplicationRig.Stone));
        rig.Runtime.Simulation.ApplyServerOperation(new BlockSetOp(id, new CellPos(2, 0, 0), ReplicationRig.Stone));
        rig.Step(3);
        Assert.Equal(2, rig.Replication.Constructs.BufferedOperations(alice.Player));
        Assert.True(rig.Replication.Constructs.StagedBytes(alice.Player) >= 40 * 16384);
        budget.Open = true;
        rig.Step(6);
        var all = alice.Received.Where(m => m is ConstructState or OperationApplied).ToList();
        var lastPart = all.FindLastIndex(m => m is ConstructState);
        var firstOperation = all.FindIndex(m => m is OperationApplied);
        Assert.True(firstOperation > lastPart, "operations must follow every part");
        var parts = all.OfType<ConstructState>().ToList();
        Assert.All(parts, p => Assert.InRange(p.Chunks.Count, 1, 12));
        Assert.Equal(parts.Count, parts[0].PartCount);
        Assert.Equal(40, parts.Sum(p => p.Chunks.Count));
        Assert.Equal(new uint[] { 41, 42 }, all.OfType<OperationApplied>().Select(o => o.ConstructChanges[0].Version).ToArray());
        Assert.Equal(0, rig.Replication.Constructs.BufferedOperations(alice.Player));
    }

    [Fact]
    public async Task Resync_WhileTransferRuns_RestartsWithNewPartZero()
    {
        var gate = new GatedSendBudget();
        await using var rig = await ReplicationRig.StartAsync(sendBudget: gate);
        var alice = await rig.JoinAsync("alice", 1);
        var cells = new CellPos[300];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = new CellPos((16 * (i % 20)) + 1, 16 * (i / 20), 0);
        }

        var id = rig.AddConstruct(new Vector3(10, 4000, 10), cells);
        for (var i = 0; i < 30 && !alice.Received.OfType<ConstructState>().Any(); i++)
        {
            rig.Step();
        }

        gate.Open = false;
        rig.Step(3);
        var sent = alice.Received.OfType<ConstructState>().Count(m => m.Header.Id == id);
        Assert.InRange(sent, 1, 24);
        Assert.True(rig.Replication.Constructs.IsTransferring(alice.Player, id));

        alice.Session.Send(new ResyncRequest(id));
        rig.Step(3);
        gate.Open = true;
        rig.Step(120);
        var parts = alice.Received.OfType<ConstructState>().Where(m => m.Header.Id == id).ToList();
        Assert.True(parts.Count(m => m.PartIndex == 0) >= 2, "a request during a transfer must restart it, not be refused");
        var lastRestart = parts.FindLastIndex(m => m.PartIndex == 0);
        Assert.Equal(parts[lastRestart].PartCount, parts.Count - lastRestart);
        Assert.False(rig.Replication.Constructs.IsTransferring(alice.Player, id));
    }

    [Fact]
    public async Task Interest_BodyDisappears_DespawnsEveryKnownEntityBeforeForgetting()
    {
        await using var rig = await ReplicationRig.StartAsync();
        var alice = await rig.JoinAsync("alice", 1);
        var bob = await rig.JoinAsync("bob", 2, new Vector3(20, 4000, 0));
        var construct = rig.AddConstruct(new Vector3(10, 4000, 10));
        rig.Step(30);
        var bobBody = rig.EntityOf(bob.Player);
        Assert.True(rig.Replication.Knows(alice.Player, bobBody));
        Assert.True(rig.Replication.Knows(alice.Player, construct));

        rig.Characters.States.Remove(alice.Player);
        rig.Step(30);
        Assert.False(rig.Replication.Knows(alice.Player, bobBody));
        var despawned = alice.Received.OfType<EntityDespawn>().Select(m => m.Entity).ToList();
        Assert.Contains(bobBody, despawned);
        Assert.Contains(construct, despawned);

        rig.Place(alice.Player, new Vector3(0, 4000, 0));
        rig.Step(30);
        Assert.True(rig.Replication.Knows(alice.Player, construct));
        Assert.Equal(2, alice.Received.OfType<EntitySpawn>().Count(m => m.Entity == construct));
    }
}
