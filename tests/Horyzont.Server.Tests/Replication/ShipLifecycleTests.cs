using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Replication;

/// <summary>K5-26 decision 3: parents spawn before their children and despawn after them (also in <c>ResetKnown</c>).</summary>
public sealed class ShipLifecycleTests
{
    private static readonly Vector3 _here = new(1_000_000f, 0f, 0f);

    private static int IndexOf<T>(RigClient client, EntityId entity)
        where T : Message =>
        client.Received.FindIndex(m => m is T && (m is EntitySpawn s ? s.Entity == entity : m is EntityDespawn d && d.Entity == entity));

    /// <summary>
    /// Alice rides ship A in the system frame; Bob and Carol (who joined before the ship existed, so their entity ids are lower) ride
    /// ship B, 20 km away.
    /// </summary>
    private static async Task<(ReplicationRig Rig, RigClient Alice, EntityId Ship, EntityId[] Passengers)> StartAsync()
    {
        var rig = await ReplicationRig.StartAsync(() => WorldFactory.CreateWithStarSystem(0));
        Assert.True(rig.Runtime.State.Planets.TryGet(rig.Runtime.State.HomePlanet, out var home));
        var system = FrameRef.Of(home!.System);
        var alice = await rig.JoinAsync("alice", 1);
        var bob = await rig.JoinAsync("bob", 2);
        var carol = await rig.JoinAsync("carol", 3);
        var own = rig.AddShipIn(system, _here);
        var ship = rig.AddShipIn(system, _here + new Vector3(20_000f, 0, 0));
        rig.PlaceIn(alice.Player, FrameRef.Of(own), Vector3.Zero);
        rig.PlaceIn(bob.Player, FrameRef.Of(ship), new Vector3(1, 0, 0));
        rig.PlaceIn(carol.Player, FrameRef.Of(ship), new Vector3(2, 0, 0));
        rig.Step(9);
        var passengers = new[] { rig.EntityOf(bob.Player), rig.EntityOf(carol.Player) };
        Assert.All(passengers, p => Assert.True(p.Value < ship.Value));
        return (rig, alice, ship, passengers);
    }

    [Fact]
    public async Task Lifecycle_ShipSpawnBeforePassengers_DespawnAfter()
    {
        var (rig, alice, ship, passengers) = await StartAsync();
        await using var _ = rig;
        Assert.False(rig.Replication.Knows(alice.Player, ship));
        Assert.True(rig.Runtime.State.Constructs.TryGet(ship, out var construct));
        Assert.All(passengers, p => Assert.False(rig.Replication.Knows(alice.Player, p)));

        alice.Received.Clear();
        rig.MoveShip(ship, construct!.Parent, _here + new Vector3(5_000f, 0, 0));
        rig.Step(9);
        var shipSpawn = IndexOf<EntitySpawn>(alice, ship);
        Assert.True(shipSpawn >= 0);
        foreach (var passenger in passengers)
        {
            Assert.True(IndexOf<EntitySpawn>(alice, passenger) > shipSpawn, "a passenger spawned before its ship");
        }

        alice.Received.Clear();
        rig.MoveShip(ship, construct.Parent, _here + new Vector3(30_000f, 0, 0));
        rig.Step(9);
        var shipDespawn = IndexOf<EntityDespawn>(alice, ship);
        Assert.True(shipDespawn >= 0);
        foreach (var passenger in passengers)
        {
            var at = IndexOf<EntityDespawn>(alice, passenger);
            Assert.True(at >= 0 && at < shipDespawn, "a passenger despawned after its ship");
        }
    }

    [Fact]
    public async Task Lifecycle_ResetKnown_ChildrenFirst()
    {
        var (rig, alice, ship, passengers) = await StartAsync();
        await using var _ = rig;
        Assert.True(rig.Runtime.State.Constructs.TryGet(ship, out var construct));
        rig.MoveShip(ship, construct!.Parent, _here + new Vector3(5_000f, 0, 0));
        rig.Step(9);
        Assert.True(rig.Replication.Knows(alice.Player, passengers[0]));

        // Without a body Alice's interest set is withdrawn: passengers before their ship.
        alice.Received.Clear();
        rig.Characters.States.Remove(alice.Player);
        rig.Step(9);
        var shipDespawn = IndexOf<EntityDespawn>(alice, ship);
        Assert.True(shipDespawn >= 0);
        foreach (var passenger in passengers)
        {
            var at = IndexOf<EntityDespawn>(alice, passenger);
            Assert.True(at >= 0 && at < shipDespawn, "a passenger despawned after its ship");
        }
    }
}
