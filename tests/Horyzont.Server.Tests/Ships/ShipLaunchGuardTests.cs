using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.World.Ships;
using Horyzont.Testing.Ships;

namespace Horyzont.Server.Tests.Ships;

/// <summary>K5-25 decision 9 (R3): persons in the hull at a launch attempt.</summary>
public sealed class ShipLaunchGuardTests
{
    private static readonly ShipControl _launch = new(0, 127, 0, 0, 0, 0, ShipButtons.Assist);

    /// <summary>A scripted pilot that holds the gear up (a launch attempt) from the first tick.</summary>
    private static SeatRig Rig(Func<EntityId, int, ShipControl?>? script = null) =>
        new(control: new ScriptedControlSource(script ?? ((_, _) => _launch)));

    private static readonly Vector3 _inHull = new(2.5f, 2.05f, 2.5f);

    private static ShipLaunchBlock Block(SeatRig rig)
    {
        Assert.True(rig.Flight.Flight.TryGetStatus(FlightRig.Ship, out var status));
        return status.LaunchBlock;
    }

    private static bool Parked(SeatRig rig) => (rig.Flight.Record().Flags & ShipStateFlags.Parked) != 0;

    [Fact]
    public void ShipLaunchGuard_PersonInHull_SeatedOnFreeSeat()
    {
        var rig = Rig();
        var person = rig.Join(2);
        rig.StandAt(person, _inHull);

        rig.Ticks(1);

        Assert.Equal(SeatRig.SeatA, rig.Player(person).Seat);
        Assert.Equal(ShipLaunchBlock.None, Block(rig));
        rig.Ticks(60);
        Assert.False(Parked(rig));
    }

    [Fact]
    public void ShipLaunchGuard_NoFreeSeat_BlocksPersonInHull()
    {
        var rig = Rig();
        var persons = new[] { rig.Join(2), rig.Join(3), rig.Join(4), rig.Join(5) };
        foreach (var p in persons)
        {
            rig.StandAt(p, _inHull);
        }

        rig.Ticks(60);

        Assert.Equal(SeatRig.SeatA, rig.Player(persons[0]).Seat);
        Assert.Equal(SeatRig.SeatB, rig.Player(persons[1]).Seat);
        Assert.Equal(SeatRig.Nav, rig.Player(persons[2]).Seat);
        Assert.Null(rig.Player(persons[3]).Seat);
        Assert.Equal(ShipLaunchBlock.PersonInHull, Block(rig));
        Assert.True(Parked(rig));
        Assert.InRange(rig.Flight.HeightOverGround(), -0.5, 0.5);
    }

    [Fact]
    public void ShipLaunchGuard_OfflinePersonInHull_Blocks()
    {
        var rig = Rig();
        var person = rig.Join(2);
        rig.StandAt(person, _inHull);
        rig.Leave(person);

        rig.Ticks(10);

        Assert.Null(rig.Player(person).Seat);
        Assert.Equal(ShipLaunchBlock.PersonInHull, Block(rig));
        Assert.True(Parked(rig));
    }

    [Fact]
    public void ShipLaunchGuard_PersonLeaves_LaunchAllowed()
    {
        var rig = Rig();
        var person = rig.Join(2);
        rig.StandAt(person, _inHull);
        rig.Leave(person);
        rig.Ticks(5);
        Assert.Equal(ShipLaunchBlock.PersonInHull, Block(rig));

        rig.StandAt(person, new Vector3(30f, 0f, 30f));
        rig.Ticks(40);

        Assert.Equal(ShipLaunchBlock.None, Block(rig));
        Assert.False(Parked(rig));
    }

    [Fact]
    public void ShipLaunchGuard_FuelReserve_NoSeatingAndFuelReserveReported()
    {
        var rig = Rig();
        Assert.True(rig.Flight.Flight.SetFuel(FlightRig.Ship, 150f));
        var person = rig.Join(2);
        rig.StandAt(person, _inHull);

        rig.Ticks(5);

        Assert.Null(rig.Player(person).Seat);
        Assert.Equal(ShipLaunchBlock.FuelReserve, Block(rig));
        Assert.Equal(0, rig.Guard.Evaluations);
    }

    [Fact]
    public void ShipLaunchGuard_AtMostOncePer30Ticks()
    {
        var rig = Rig();
        var person = rig.Join(2);
        rig.StandAt(person, _inHull);
        rig.Leave(person);

        rig.Ticks(30);
        Assert.Equal(1, rig.Guard.Evaluations);
        Assert.True(Parked(rig));
        Assert.InRange(rig.Flight.HeightOverGround(), -0.5, 0.5);

        rig.Ticks(1);
        Assert.Equal(2, rig.Guard.Evaluations);
    }

    [Fact]
    public void ShipLaunchGuard_AutolandControl_NoGuard()
    {
        var rig = Rig((_, _) => null);
        var person = rig.Join(2);
        rig.StandAt(person, _inHull);

        rig.Ticks(10);

        Assert.Equal(0, rig.Guard.Evaluations);
        Assert.Null(rig.Player(person).Seat);
        Assert.Equal(ShipLaunchBlock.None, Block(rig));
    }
}
