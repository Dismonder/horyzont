using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Server.Replication;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Replication;

/// <summary>K5-26 decisions 1–2: interest ranges by kind and region, passengers with the subject of their ship.</summary>
public sealed class ShipInterestTests
{
    private static readonly Vector3 _here = new(1_000_000f, 0f, 0f);

    private static Task<ReplicationRig> StartAsync() => ReplicationRig.StartAsync(() => WorldFactory.CreateWithStarSystem(0));

    private static FrameRef SystemFrame(ReplicationRig rig)
    {
        Assert.True(rig.Runtime.State.Planets.TryGet(rig.Runtime.State.HomePlanet, out var home));
        return FrameRef.Of(home!.System);
    }

    /// <summary>Alice rides ship A in the system frame; ship B is placed at a distance from A's origin along X.</summary>
    private static async Task<(ReplicationRig Rig, RigClient Alice, EntityId Other)> TwoShipsInSystemAsync()
    {
        var rig = await StartAsync();
        var system = SystemFrame(rig);
        var alice = await rig.JoinAsync("alice", 1);
        var own = rig.AddShipIn(system, _here);
        rig.PlaceIn(alice.Player, FrameRef.Of(own), Vector3.Zero);
        var other = rig.AddShipIn(system, _here + new Vector3(20_000f, 0, 0));
        rig.Step(9);
        return (rig, alice, other);
    }

    private static float Radius => 1.7320508f;

    [Fact]
    public void Ranges_Default_PlanetEverything500_SystemShips10km()
    {
        var ranges = ReplicationRanges.Default;
        var planet = FrameRef.Of(new PlanetId(3));
        var system = FrameRef.Of(new SystemId(1));
        Assert.Equal(500f, ranges.For(planet, isShip: true));
        Assert.Equal(500f, ranges.For(planet, isShip: false));
        Assert.Equal(10_000f, ranges.For(system, isShip: true));
        Assert.Equal(500f, ranges.For(system, isShip: false));
        Assert.Equal(250f, ReplicationRanges.Uniform(250f).For(system, isShip: true));
    }

    [Fact]
    public async Task Interest_ShipInSystem_EntersAt10km_LeavesBeyond10_5km()
    {
        var (rig, alice, other) = await TwoShipsInSystemAsync();
        await using var _ = rig;
        var system = SystemFrame(rig);
        Assert.False(rig.Replication.Knows(alice.Player, other));

        // 10.1 km from the sphere surface: still out; 9.9 km: in.
        rig.MoveShip(other, system, _here + new Vector3(10_100f + Radius, 0, 0));
        rig.Step(9);
        Assert.False(rig.Replication.Knows(alice.Player, other));
        rig.MoveShip(other, system, _here + new Vector3(9_900f + Radius, 0, 0));
        rig.Step(9);
        Assert.True(rig.Replication.Knows(alice.Player, other));

        // The exit threshold is 10 km + max(50 m, 5 %) = 10.5 km.
        rig.MoveShip(other, system, _here + new Vector3(10_450f + Radius, 0, 0));
        rig.Step(9);
        Assert.True(rig.Replication.Knows(alice.Player, other));
        rig.MoveShip(other, system, _here + new Vector3(10_550f + Radius, 0, 0));
        rig.Step(9);
        Assert.False(rig.Replication.Knows(alice.Player, other));
        Assert.Contains(alice.Received, m => m is EntityDespawn d && d.Entity == other);
    }

    [Fact]
    public async Task Interest_ShipOnPlanet_500m()
    {
        await using var rig = await StartAsync();
        var home = FrameRef.Of(rig.Runtime.State.HomePlanet);
        var alice = await rig.JoinAsync("alice", 1, new Vector3(0, 4000, 0), rig.Runtime.State.HomePlanet);
        var ship = rig.AddShipIn(home, new Vector3(0, 4000, 600f + Radius));
        rig.Step(9);
        Assert.False(rig.Replication.Knows(alice.Player, ship));
        rig.MoveShip(ship, home, new Vector3(0, 4000, 490f + Radius));
        rig.Step(9);
        Assert.True(rig.Replication.Knows(alice.Player, ship));
        rig.MoveShip(ship, home, new Vector3(0, 4000, 545f + Radius));
        rig.Step(9);
        Assert.True(rig.Replication.Knows(alice.Player, ship));
        rig.MoveShip(ship, home, new Vector3(0, 4000, 555f + Radius));
        rig.Step(9);
        Assert.False(rig.Replication.Knows(alice.Player, ship));
    }

    [Fact]
    public async Task Interest_PassengersEnterAndLeaveWithShip()
    {
        var (rig, alice, other) = await TwoShipsInSystemAsync();
        await using var _ = rig;
        var system = SystemFrame(rig);
        var bob = await rig.JoinAsync("bob", 2);
        var carol = await rig.JoinAsync("carol", 3);
        var bobBody = rig.EntityOf(bob.Player);
        var carolBody = rig.EntityOf(carol.Player);

        // Bob at the far end of B, Carol at the near end, both 300 m from B's origin: on their own one would be in range and one
        // out at the edge; with the subject of the ship both follow it.
        rig.PlaceIn(bob.Player, FrameRef.Of(other), new Vector3(300f, 0, 0));
        rig.PlaceIn(carol.Player, FrameRef.Of(other), new Vector3(-300f, 0, 0));
        rig.MoveShip(other, system, _here + new Vector3(9_900f + Radius, 0, 0));
        rig.Step(9);
        Assert.True(rig.Replication.Knows(alice.Player, other));
        Assert.True(rig.Replication.Knows(alice.Player, bobBody));
        Assert.True(rig.Replication.Knows(alice.Player, carolBody));

        rig.MoveShip(other, system, _here + new Vector3(10_400f + Radius, 0, 0));
        rig.Step(9);
        Assert.True(rig.Replication.Knows(alice.Player, bobBody));
        Assert.True(rig.Replication.Knows(alice.Player, carolBody));

        rig.MoveShip(other, system, _here + new Vector3(10_600f + Radius, 0, 0));
        rig.Step(9);
        Assert.False(rig.Replication.Knows(alice.Player, other));
        Assert.False(rig.Replication.Knows(alice.Player, bobBody));
        Assert.False(rig.Replication.Knows(alice.Player, carolBody));

        // Bob and Carol, in B, see Alice's ship and Alice the same way.
        rig.MoveShip(other, system, _here + new Vector3(5_000f, 0, 0));
        rig.Step(9);
        Assert.True(rig.Replication.Knows(bob.Player, rig.EntityOf(alice.Player)));
        Assert.True(rig.Replication.Knows(carol.Player, rig.EntityOf(alice.Player)));
    }
}
