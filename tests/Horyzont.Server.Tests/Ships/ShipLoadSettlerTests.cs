using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.World.Ships;
using Horyzont.Server.Ships;
using Horyzont.Server.Space;
using Horyzont.Testing.Ships;

namespace Horyzont.Server.Tests.Ships;

/// <summary>K5-24 decision 18: ships loaded unparked.</summary>
public sealed class ShipLoadSettlerTests
{
    private static ShipStateRecord Flying => new(FlightRig.Ship, 500f, new Vector3(3, -20, 0), new Vector3(0, 0.2f, 0), ShipStateFlags.Assist);

    [Fact]
    public void ShipLoadSettler_UnparkedInZone_PlacedOnGroundAndParked()
    {
        var rig = new FlightRig(heightOverGround: 80, state: Flying);

        rig.Ticks(1);

        var record = rig.Record();
        Assert.True((record.Flags & ShipStateFlags.Parked) != 0);
        Assert.True((record.Flags & ShipStateFlags.GearDown) != 0);
        Assert.Equal(Vector3.Zero, record.LinearVelocity);
        Assert.Equal(Vector3.Zero, record.AngularVelocity);
        Assert.Equal(500f, record.Fuel);
        Assert.InRange(rig.HeightOverGround(), -0.5, 0.5);
        Assert.Contains(rig.Logger.Lines, l => l.StartsWith("Information:2402", StringComparison.Ordinal));
    }

    [Fact]
    public void ShipLoadSettler_InSystem_StopsInPlace()
    {
        var settler = new ShipLoadSettler(
            new SphereShipCollider(new SphereGroundCollider(), null!), new HomeOnlyGate(FlightFixtures.Home.Id), FlightRules.Default, new FlightRig.ListLogger());
        var motion = new ShipMotion
        {
            Pose = new PoseD(new Vector3D(100_000, 5, 7), QuaternionD.Identity),
            LinearVelocity = new Vector3(500, 0, 0),
            AngularVelocity = Vector3.UnitY,
            Flags = ShipFlightFlags.Assist,
        };
        var pose = motion.Pose;

        var result = settler.Settle(FlightRig.Ship, ref motion, FlightFixtures.Thirty(), FlightFixtures.Void(), planet: null);

        Assert.Equal(ShipSettleResult.Settled, result);
        Assert.Equal(pose, motion.Pose);
        Assert.Equal(Vector3.Zero, motion.LinearVelocity);
        Assert.Equal(Vector3.Zero, motion.AngularVelocity);
        Assert.Equal(ShipFlightFlags.None, motion.Flags & ShipFlightFlags.Parked);
    }

    [Fact]
    public void ShipLoadSettler_WaitsForGateReady()
    {
        var readiness = new FakeTerrainReadiness { Answer = PlanetTerrainReadiness.Streaming };
        var rig = new FlightRig(heightOverGround: 80, state: Flying, gate: Gate(readiness));
        var origin = rig.Construct().Origin;

        rig.Ticks(30);

        Assert.Equal(origin, rig.Construct().Origin);
        Assert.Equal(Vector3.Zero, rig.Record().LinearVelocity);
        Assert.Equal((ShipStateFlags)0, rig.Record().Flags & ShipStateFlags.Parked);

        readiness.Answer = PlanetTerrainReadiness.Ready;
        rig.Ticks(1);

        Assert.True((rig.Record().Flags & ShipStateFlags.Parked) != 0);
        Assert.InRange(rig.HeightOverGround(), -0.5, 0.5);
    }

    [Fact]
    public void ShipLoadSettler_GateUnavailable_HeldInPlaceAndRetries()
    {
        var readiness = new FakeTerrainReadiness { Answer = PlanetTerrainReadiness.Faulted };
        var gate = Gate(readiness);
        var rig = new FlightRig(heightOverGround: 80, state: Flying, gate: gate);
        var origin = rig.Construct().Origin;

        rig.Ticks(12 * FlightRig.TickRate);

        Assert.Equal(origin, rig.Construct().Origin);
        Assert.True(rig.Flight.TryGetStatus(FlightRig.Ship, out var status));
        Assert.True((status.Flags & ShipFlightFlags.TerrainUnavailable) != 0);
        Assert.InRange(gate.Queries, 2, 4);
        Assert.InRange(gate.Warnings, 1, 2);

        readiness.Answer = PlanetTerrainReadiness.Ready;
        rig.Ticks(PlanetTerrainShipGate.RetrySeconds * FlightRig.TickRate);
        Assert.True((rig.Record().Flags & ShipStateFlags.Parked) != 0);
    }

    [Fact]
    public void ShipLoadSettler_ColumnNotReadyUnderTheDrop_Waits()
    {
        // The flight column (64 m at v = 0) is ready; the ground 300 m under the ship, where the drop ends, is not.
        var readiness = new FakeTerrainReadiness { By = GroundNotReady };
        var rig = new FlightRig(heightOverGround: 300, state: Flying, gate: Gate(readiness, maxRelief: 10f));
        var origin = rig.Construct().Origin;

        rig.Ticks(30);

        Assert.Equal(origin, rig.Construct().Origin);
        Assert.Equal(Vector3.Zero, rig.Record().LinearVelocity);
        Assert.Equal((ShipStateFlags)0, rig.Record().Flags & ShipStateFlags.Parked);

        readiness.By = null;
        rig.Ticks(1);

        Assert.True((rig.Record().Flags & ShipStateFlags.Parked) != 0);
        Assert.InRange(rig.HeightOverGround(), -0.5, 0.5);
    }

    [Fact]
    public void ShipLoadSettler_ColumnUnderTheDropNeverReady_ReleasedToFlightAfterTimeout()
    {
        // Nothing loads the ground far under a ship held at v = 0: after the readiness timeout the flight takes over (autoland
        // through the flight gate) instead of holding the ship forever.
        var readiness = new FakeTerrainReadiness { By = GroundNotReady };
        var rig = new FlightRig(heightOverGround: 300, state: Flying, gate: Gate(readiness, maxRelief: 10f));
        var origin = rig.Construct().Origin;
        var timeout = (int)FlightRules.Default.ReadinessTimeoutSeconds * FlightRig.TickRate;

        rig.Ticks(timeout - 10);
        Assert.Equal(origin, rig.Construct().Origin);

        rig.Ticks(10 + FlightRig.TickRate);
        Assert.True(rig.Construct().Origin.Y < origin.Y, "the released ship descends");
        Assert.Equal((ShipStateFlags)0, rig.Record().Flags & ShipStateFlags.Parked);
    }

    private static PlanetTerrainReadiness GroundNotReady(Vector3 center, float radius) =>
        center.Length() - radius < FlightFixtures.Home.RadiusMeters + 100 ? PlanetTerrainReadiness.Streaming : PlanetTerrainReadiness.Ready;

    private static PlanetTerrainShipGate Gate(FakeTerrainReadiness readiness, float maxRelief) =>
        new(readiness, ShipFixtures.CreateWorld(null), FlightRules.Default, maxRelief, FlightRig.TickRate, new FlightRig.ListLogger());

    private static PlanetTerrainShipGate Gate(FakeTerrainReadiness readiness) =>
        new(readiness, ShipFixtures.CreateWorld(null), FlightRules.Default, 300f, FlightRig.TickRate, new FlightRig.ListLogger());
}
