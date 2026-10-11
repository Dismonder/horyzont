using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.World.Ships;
using Horyzont.Server.Ships;

namespace Horyzont.Server.Tests.Ships;

/// <summary>K5-24 decisions 10, 12 and 17: the flight of ships on the server.</summary>
public sealed class ShipFlightSystemTests
{
    private static readonly ShipControl _climb = new(0, 127, 0, 0, 0, 0, ShipButtons.Assist);

    [Fact]
    public void ShipFlightSystem_NoPilot_Autolands()
    {
        // Two seconds of climbing, then nobody at the controls: the ship autolands and parks.
        var rig = new FlightRig(control: new Testing.Ships.ScriptedControlSource((_, n) => n < 120 ? _climb : null));
        Assert.True(rig.Flight.SetFuel(FlightRig.Ship, 2000f));

        rig.Ticks(120);
        var top = rig.HeightOverGround();
        Assert.True(top > 5, $"the ship climbed ({top:F1} m)");
        Assert.True(rig.Flight.TryGetStatus(FlightRig.Ship, out var flying));
        Assert.Equal(ShipFlightFlags.None, flying.Flags & ShipFlightFlags.Parked);

        var ticks = 0;
        while (ticks < 60 * FlightRig.TickRate && (rig.Record().Flags & ShipStateFlags.Parked) == 0)
        {
            rig.Ticks(1);
            ticks++;
        }

        Assert.True((rig.Record().Flags & ShipStateFlags.Parked) != 0, $"parked after {ticks} ticks");
        Assert.True(rig.Flight.TryGetStatus(FlightRig.Ship, out var status));
        Assert.True((status.Flags & ShipFlightFlags.Autoland) != 0);
        Assert.Equal(Vector3.Zero, rig.Record().LinearVelocity);
        Assert.InRange(rig.HeightOverGround(), -0.5, 0.5);
        var c = rig.Construct();
        Assert.InRange(MathF.Abs(c.Origin.X) + MathF.Abs(c.Origin.Z), 0, 5);
    }

    [Fact]
    public void ShipFlightSystem_TryGetStatus_FuelLimitAndReserveBlock()
    {
        var rig = new FlightRig();
        Assert.False(rig.Flight.TryGetStatus(FlightRig.Ship, out _));
        rig.Flight.SetFuel(FlightRig.Ship, 150f);

        rig.Ticks(1);

        Assert.True(rig.Flight.TryGetStatus(FlightRig.Ship, out var status));
        Assert.Equal(150f, status.Fuel);
        Assert.Equal(2000f, status.FuelCapacity);
        Assert.True((status.Flags & ShipFlightFlags.Parked) != 0);
        Assert.Equal(ShipLaunchBlock.FuelReserve, status.LaunchBlock);
        Assert.Equal((float)FlightRules.Default.MinSpeedLimit, status.SpeedLimit);

        rig.Flight.SetFuel(FlightRig.Ship, 1000f);
        rig.Ticks(1);
        Assert.True(rig.Flight.TryGetStatus(FlightRig.Ship, out var refuelled));
        Assert.Equal(ShipLaunchBlock.None, refuelled.LaunchBlock);
        Assert.False(rig.Flight.TryGetStatus(new EntityId(999), out _));
    }

    [Fact]
    public void ShipFlightSystem_HomeOnlyGate_NoDescentBelow2RElsewhere()
    {
        // The gate treats the home planet as "elsewhere"; the ship falls from 2.2 R without control and must hold at 2 R.
        var rig = new FlightRig(heightOverGround: 1.2 * Testing.Ships.FlightFixtures.Home.RadiusMeters, gate: new HomeOnlyGate(new PlanetId(999)),
            control: new Testing.Ships.ScriptedControlSource((_, _) => default(ShipControl)));
        rig.Flight.SetFuel(FlightRig.Ship, 2000f);

        var lowest = double.MaxValue;
        for (var i = 0; i < 90 * FlightRig.TickRate; i++)
        {
            rig.Ticks(1);
            lowest = Math.Min(lowest, rig.ComDistance());
        }

        Assert.True(lowest >= (2 * rig.Radius) - 1, $"lowest {lowest:F1} m, 2 R = {2 * rig.Radius} m");
        Assert.True(lowest < 2.1 * rig.Radius, "the ship did fall down to the floor");
        Assert.True(rig.Flight.TryGetStatus(FlightRig.Ship, out var status));
        Assert.True((status.Flags & ShipFlightFlags.TerrainHold) != 0);
    }

    [Fact]
    public void ShipFlightSystem_HeaderChangedElsewhere_RestartsFromHeaderKeepsVelocity()
    {
        // A zone transition or a mode switch changes the header outside the flight: the motion restarts from the stored pose and the
        // velocity in the parent frame carries over (the frames of the early access share their axes).
        var rig = new FlightRig(control: new Testing.Ships.ScriptedControlSource((_, _) => _climb));
        rig.Flight.SetFuel(FlightRig.Ship, 2000f);
        rig.Ticks(90);
        var before = rig.Record().LinearVelocity;
        Assert.True(before.Y > 1, $"climbing at {before.Y:F2} m/s");

        var c = rig.Construct();
        var moved = c.Origin + new Vector3(50, 0, 0);
        Assert.True(rig.Sim.UpdateConstructKinematics(c.Id, c.Parent, c.ParentSeq, moved, c.Rotation));
        rig.Ticks(1);

        var after = rig.Construct().Origin;
        Assert.InRange(after.X, 49.9f, 50.1f);
        Assert.InRange(rig.Record().LinearVelocity.Y, before.Y - 1, before.Y + 1);
    }

    [Fact]
    public void ShipFlightSystem_EightShips_TickAllocatesZero()
    {
        var hover = new ShipControl(0, 0, 0, 0, 0, 0, ShipButtons.Assist);
        var rig = new FlightRig(heightOverGround: 200, ships: 8, control: new Testing.Ships.ScriptedControlSource((_, _) => hover));
        foreach (var id in rig.Ids)
        {
            Assert.True(rig.Flight.SetFuel(id, 2000f));
        }

        rig.Ticks(200);
        Assert.Equal(8, rig.Flight.Count);

        var before = GC.GetAllocatedBytesForCurrentThread();
        rig.Ticks(100);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        foreach (var id in rig.Ids)
        {
            Assert.True((rig.Record(id).Flags & ShipStateFlags.Parked) == 0);
        }
    }

    [Fact]
    public void ShipConsoleLine_FlightPart_FixedFormat()
    {
        var status = new ShipFlightStatus(512.34f, 2000f, 250f, ShipFlightFlags.Parked | ShipFlightFlags.TerrainHold, ShipLaunchBlock.None);

        Assert.Equal("fuel=512.3/2000 v=12.5 limit=250 parked=yes gate=hold", Console.ConsoleCommandProcessor.FormatFlight(status, 12.5f));
    }

    [Fact]
    public void ShipFlightSystem_RegisteredBeforeZoneTransitions()
    {
        var path = Path.Combine(RepoRoot(), "game", "src", "Boot", "GameComposition.Space.cs");
        var text = File.ReadAllText(path);
        var flight = text.IndexOf("ConfigureFlight(server)", StringComparison.Ordinal);
        var zones = text.IndexOf("server.AddTickParticipant(ZoneTransitions)", StringComparison.Ordinal);

        Assert.True(flight >= 0 && zones >= 0, "both registrations are in ConfigureSpace");
        Assert.True(flight < zones, "the flight runs before the zone transitions in phase Frames");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Horyzont.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
