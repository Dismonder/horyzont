using Horyzont.Core.Ids;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.World.Ships;

namespace Horyzont.Server.Tests.Ships;

/// <summary>K5-25 decisions 10 and 13 (R1): one pilot input slot per server tick.</summary>
public sealed class PilotControlSourceTests
{
    private static ShipControl C(int seq, ShipButtons buttons = ShipButtons.Assist | ShipButtons.LandingGear) =>
        new((sbyte)(seq % 120), 0, 0, 0, 0, 0, buttons);

    private static (SeatRig Rig, PlayerId Pilot) Piloted()
    {
        var rig = new SeatRig(guard: false);
        var pilot = rig.Join(1);
        rig.Sit(pilot, SeatRig.Cockpit);
        return (rig, pilot);
    }

    private static ShipControl Take(SeatRig rig, ulong tick)
    {
        Assert.True(rig.Pilot.TryGetControl(FlightRig.Ship, new Tick(tick), out var control));
        return control;
    }

    [Fact]
    public void PilotControlSource_OneInputPerTick()
    {
        var (rig, pilot) = Piloted();
        for (uint s = 1; s <= 5; s++)
        {
            rig.Inputs.Add(pilot, s, C((int)s));
        }

        // Anchored two slots behind the newest input: 3, 4, 5, one per tick.
        Assert.Equal(C(3), Take(rig, 1));
        Assert.Equal(C(4), Take(rig, 2));
        Assert.Equal(C(5), Take(rig, 3));
        Assert.True(rig.Pilot.TryGetLastProcessedSeq(FlightRig.Ship, out var player, out var seq));
        Assert.Equal(pilot, player);
        Assert.Equal(5u, seq);
        Assert.Equal(3, rig.Pilot.Stats.Consumed);
    }

    [Fact]
    public void PilotControlSource_NoPilot_ReturnsFalseAutoland()
    {
        var rig = new SeatRig(guard: false);
        var standing = rig.Join(1);
        rig.Inputs.Add(standing, 1, C(1));

        Assert.False(rig.Pilot.TryGetControl(FlightRig.Ship, new Tick(1), out _));
        rig.Sit(standing, SeatRig.SeatA);
        Assert.False(rig.Pilot.TryGetControl(FlightRig.Ship, new Tick(2), out _));
    }

    [Fact]
    public void PilotControlSource_PilotOffline_ReturnsFalse()
    {
        var (rig, pilot) = Piloted();
        rig.Inputs.Add(pilot, 1, C(1));
        Take(rig, 1);

        rig.Leave(pilot);

        Assert.False(rig.Pilot.TryGetControl(FlightRig.Ship, new Tick(2), out _));
        Assert.False(rig.Pilot.TryGetLastProcessedSeq(FlightRig.Ship, out _, out _));
    }

    [Fact]
    public void PilotControlSource_MissingSlot_SubstitutesLastAndAdvances()
    {
        var (rig, pilot) = Piloted();
        rig.Inputs.Add(pilot, 1, C(1));
        rig.Inputs.Add(pilot, 2, C(2));
        rig.Inputs.Add(pilot, 3, C(3));
        rig.Inputs.Add(pilot, 5, C(5));

        Assert.Equal(C(3), Take(rig, 1));
        Assert.Equal(C(3), Take(rig, 2)); // slot 4 lost: the last control again
        Assert.True(rig.Pilot.TryGetLastProcessedSeq(FlightRig.Ship, out _, out var seq));
        Assert.Equal(4u, seq);
        Assert.Equal(C(5), Take(rig, 3));
        Assert.Equal(1, rig.Pilot.Stats.Substituted);
    }

    [Fact]
    public void PilotControlSource_LateInputForConsumedSlot_Dropped()
    {
        var (rig, pilot) = Piloted();
        rig.Inputs.Add(pilot, 1, C(1));
        rig.Inputs.Add(pilot, 2, C(2));
        rig.Inputs.Add(pilot, 3, C(3));
        Assert.Equal(C(1), Take(rig, 1));
        Assert.Equal(C(2), Take(rig, 2));
        Assert.Equal(C(3), Take(rig, 3));
        Assert.Equal(C(3), Take(rig, 4)); // slot 4 substituted

        rig.Inputs.Add(pilot, 4, C(4));
        rig.Inputs.Add(pilot, 5, C(5));

        Assert.Equal(1, rig.Inputs.ShipInputLateCount(pilot));
        Assert.Equal(C(5), Take(rig, 5));
    }

    [Fact]
    public void PilotControlSource_FifteenMissing_NeutralAxes()
    {
        var (rig, pilot) = Piloted();
        var thrust = new ShipControl(50, 60, 70, 10, 20, 30, ShipButtons.Assist);
        rig.Inputs.Add(pilot, 1, thrust);
        Take(rig, 1);

        for (ulong t = 2; t <= 16; t++)
        {
            Assert.Equal(thrust, Take(rig, t));
        }

        Assert.Equal(new ShipControl(0, 0, 0, 0, 0, 0, ShipButtons.Assist), Take(rig, 17));
        Assert.Equal(16, rig.Pilot.Stats.Substituted);
    }

    [Fact]
    public void PilotControlSource_ClientAheadOrBehind_Reanchors()
    {
        var (rig, pilot) = Piloted();
        rig.Inputs.Add(pilot, 1, C(1));
        Take(rig, 1);

        // Ahead: the client jumps 28 slots ahead; three missing slots with later inputs buffered re-anchor the slots.
        var seq = 30u;
        ulong tick = 2;
        while (rig.Pilot.Stats.Reanchors == 0 && tick < 200)
        {
            rig.Inputs.Add(pilot, seq, C((int)seq));
            seq++;
            Take(rig, tick++);
        }

        Assert.Equal(1, rig.Pilot.Stats.Reanchors);
        Assert.InRange(tick, 4UL, 8UL);

        // Behind: the client falls back below the consumed slots; its inputs arrive late.
        rig.Pilot.TryGetLastProcessedSeq(FlightRig.Ship, out _, out var processed);
        var back = processed - 10;
        for (var i = 0; i < 8; i++)
        {
            rig.Inputs.Add(pilot, back, C((int)back));
            back++;
            rig.Pilot.TryGetControl(FlightRig.Ship, new Tick(tick++), out _);
        }

        Assert.Equal(2, rig.Pilot.Stats.Reanchors);
    }

    [Fact]
    public void PilotControlSource_NonPilotInputs_DroppedCountedNotPilot()
    {
        var (rig, _) = Piloted();
        var passenger = rig.Join(2);
        rig.Sit(passenger, SeatRig.SeatA);
        rig.Inputs.Add(passenger, 1, C(1));

        rig.Pilot.TryGetControl(FlightRig.Ship, new Tick(1), out _);

        Assert.Equal(0, rig.Inputs.Count(passenger));
        Assert.Equal(1, rig.Pilot.Stats.NotPilotDropped);
        Assert.Contains(rig.Flight.Logger.Lines, l => l.Contains("NotPilot", StringComparison.Ordinal));
    }

    [Fact]
    public void PilotControlSource_TickAllocatesZero()
    {
        var (rig, pilot) = Piloted();
        rig.Inputs.Add(pilot, 1, C(1));
        uint next = 2;
        for (ulong t = 1; t <= 200; t++)
        {
            rig.Inputs.Add(pilot, next, C((int)next));
            next++;
            Take(rig, t);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong t = 201; t <= 1200; t++)
        {
            rig.Inputs.Add(pilot, next, C((int)next));
            next++;
            rig.Pilot.TryGetControl(FlightRig.Ship, new Tick(t), out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void PilotDisconnect_ShipAutolandsAndParks_PassengersStaySeated()
    {
        var (rig, pilot) = Piloted();
        var passenger = rig.Join(2);
        rig.Sit(passenger, SeatRig.SeatA);
        var climb = new ShipControl(0, 127, 0, 0, 0, 0, ShipButtons.Assist);
        for (uint s = 1; s <= 122; s++)
        {
            rig.Inputs.Add(pilot, s, climb);
            rig.Ticks(1);
        }

        Assert.True(rig.Flight.HeightOverGround() > 3, $"climbed {rig.Flight.HeightOverGround():F1} m");

        rig.Leave(pilot);
        var ticks = 0;
        while (ticks < 60 * FlightRig.TickRate && (rig.Flight.Record().Flags & ShipStateFlags.Parked) == 0)
        {
            rig.Ticks(1);
            ticks++;
        }

        Assert.True((rig.Flight.Record().Flags & ShipStateFlags.Parked) != 0, $"parked after {ticks} ticks");
        Assert.Equal(SeatRig.SeatA, rig.Player(passenger).Seat);
        Assert.Equal(SeatRig.Cockpit, rig.Player(pilot).Seat);
        Assert.False(rig.Player(pilot).IsOnline);
    }
}
