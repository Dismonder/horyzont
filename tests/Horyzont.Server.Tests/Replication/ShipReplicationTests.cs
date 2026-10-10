using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing.Ships;

namespace Horyzont.Server.Tests.Replication;

/// <summary>K5-20: ship-mode constructs in snapshots, interest of entities in construct frames, live construct reparenting (B1).</summary>
public sealed class ShipReplicationTests
{
    private static bool InAnySnapshot(RigClient client, EntityId entity) =>
        client.Received.OfType<Snapshot>().Any(s => s.Entities.Any(e => e.Entity == entity));

    private static int ReparentsOf(RigClient client, EntityId entity) =>
        client.Received.OfType<OperationApplied>().Count(m => m.Operation is EntityReparentOp op && op.Entity == entity);

    private static int StatesOf(RigClient client, EntityId entity) =>
        client.Received.OfType<ConstructState>().Count(m => m.Header.Id == entity);

    [Fact]
    public async Task Snapshot_ShipConstruct_IsMobile_StaticIsNot()
    {
        await using var rig = await ReplicationRig.StartAsync();
        var alice = await rig.JoinAsync("alice", 1);
        var moving = rig.AddShip(new Vector3(0, 4010, 0));
        var parked = rig.AddShip(new Vector3(50, 4010, 0));
        var still = rig.AddConstruct(new Vector3(20, 4000, 0));
        rig.Step(30);

        Assert.True(rig.Replication.Knows(alice.Player, still));
        Assert.True(InAnySnapshot(alice, moving));
        Assert.True(InAnySnapshot(alice, parked));
        Assert.False(InAnySnapshot(alice, still));

        // Every construct still gets its complete state, ships too.
        Assert.True(StatesOf(alice, moving) >= 1);
        Assert.True(StatesOf(alice, still) >= 1);
    }

    [Fact]
    public async Task Snapshot_ShipToStatic_NoDespawn()
    {
        await using var rig = await ReplicationRig.StartAsync();
        var alice = await rig.JoinAsync("alice", 1);
        var moving = rig.AddShip(new Vector3(0, 4010, 0));
        var parked = rig.AddShip(new Vector3(50, 4010, 0));
        rig.Step(30);
        Assert.True(InAnySnapshot(alice, parked));

        // The mode is read every round: a ship that parks leaves the snapshots, but not the client.
        rig.SetMode(parked, ConstructMode.Static);
        rig.Step(3);
        alice.Received.Clear();
        rig.Step(30);
        Assert.False(InAnySnapshot(alice, parked));
        Assert.True(InAnySnapshot(alice, moving));
        Assert.DoesNotContain(alice.Received, m => m is EntityDespawn d && d.Entity == parked);
        Assert.True(rig.Replication.Knows(alice.Player, parked));

        // Back to ship mode: in the snapshots again, without a second spawn.
        rig.SetMode(parked, ConstructMode.Ship);
        rig.Step(30);
        Assert.True(InAnySnapshot(alice, parked));
        Assert.DoesNotContain(alice.Received, m => m is EntitySpawn s && s.Entity == parked);
    }

    [Fact]
    public async Task Interest_EntityInConstructFrame_UsesRegionPosition()
    {
        // A ship turned a quarter around the vertical: local and planet axes differ, so only a composed position is right.
        var origin = new Vector3(0f, 4010f, 0f);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        await using var rig = await ReplicationRig.StartAsync();
        var shipFrame = FrameRef.Of(rig.AddShip(origin, rotation));
        Vector3 Local(Vector3 planet) => Vector3.Transform(planet - origin, Quaternion.Conjugate(rotation));

        var alice = await rig.JoinAsync("alice", 1, new Vector3(1000f, 4010f, 0f));
        var bob = await rig.JoinAsync("bob", 2);
        var bobBody = rig.EntityOf(bob.Player);

        // Bob in the ship frame 600 m from Alice: out of range.
        rig.PlaceIn(bob.Player, shipFrame, Local(new Vector3(400f, 4010f, 0f)));
        rig.Step(9);
        Assert.False(rig.Replication.Knows(alice.Player, bobBody));

        // 400 m: in range, spawned in the ship frame.
        rig.PlaceIn(bob.Player, shipFrame, Local(new Vector3(600f, 4010f, 0f)));
        rig.Step(9);
        Assert.True(rig.Replication.Knows(alice.Player, bobBody));
        Assert.Contains(alice.Received, m => m is EntitySpawn s && s.Entity == bobBody && s.Frame.Frame == shipFrame);

        // The viewer may be in a construct frame too: Alice at the ship origin sees Bob 450 m away on the planet, not 700 m.
        rig.PlaceIn(alice.Player, shipFrame, Vector3.Zero);
        rig.Place(bob.Player, new Vector3(0f, 4010f, 450f));
        rig.Step(9);
        Assert.True(rig.Replication.Knows(alice.Player, bobBody));
        rig.Place(bob.Player, new Vector3(0f, 4010f, 700f));
        rig.Step(9);
        Assert.False(rig.Replication.Knows(alice.Player, bobBody));
    }

    [Fact]
    public async Task Reparent_ConstructLiveLink_SentAsOperation_NoTransfer()
    {
        await using var rig = await ReplicationRig.StartAsync();
        var alice = await rig.JoinAsync("alice", 1);
        var moving = rig.AddShip(new Vector3(0, 4010, 0));
        for (var i = 0; i < 120 && !rig.Replication.Constructs.IsLive(alice.Player, moving); i++)
        {
            rig.Step();
        }

        Assert.True(rig.Replication.Constructs.IsLive(alice.Player, moving));
        var states = StatesOf(alice, moving);

        rig.Runtime.Simulation.ApplyServerOperation(new EntityReparentOp(
            moving, TestShips.HomeFrame, 1, new Vector3(1f, 4010f, 0f), Quaternion.Identity, Vector3.Zero, null));
        rig.Step(30);

        Assert.Equal(1, ReparentsOf(alice, moving));
        Assert.Equal(states, StatesOf(alice, moving));
        Assert.True(rig.Replication.Constructs.IsLive(alice.Player, moving));
    }

    [Fact]
    public async Task Reparent_ConstructPendingLink_Enqueued()
    {
        var gate = new GatedSendBudget { Open = false };
        await using var rig = await ReplicationRig.StartAsync(sendBudget: gate);
        var alice = await rig.JoinAsync("alice", 1);
        var moving = rig.AddShip(new Vector3(0, 4010, 0));
        rig.Step(6);
        Assert.True(rig.Replication.Constructs.IsTransferring(alice.Player, moving));

        rig.Runtime.Simulation.ApplyServerOperation(new EntityReparentOp(
            moving, TestShips.HomeFrame, 1, new Vector3(1f, 4010f, 0f), Quaternion.Identity, Vector3.Zero, null));
        rig.Step(3);
        Assert.Equal(0, ReparentsOf(alice, moving));
        Assert.True(rig.Replication.Constructs.IsTransferring(alice.Player, moving));

        // The transfer that completes carries the new parent sequence.
        gate.Open = true;
        rig.Step(60);
        var last = alice.Received.OfType<ConstructState>().Last(m => m.Header.Id == moving);
        Assert.Equal(1u, last.Header.ParentSeq);
        Assert.Equal(0, ReparentsOf(alice, moving));
        Assert.True(rig.Replication.Constructs.IsLive(alice.Player, moving));
    }
}
