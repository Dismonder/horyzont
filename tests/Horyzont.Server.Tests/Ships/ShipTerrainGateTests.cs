using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;
using Horyzont.Server.Ships;
using Horyzont.Server.Space;
using Horyzont.Testing.Ships;

namespace Horyzont.Server.Tests.Ships;

/// <summary>K5-24 decision 17 (README Y11): <see cref="PlanetTerrainShipGate"/> over a fake <see cref="IPlanetTerrainReadiness"/>.</summary>
public sealed class ShipTerrainGateTests
{
    private const int TickRate = 60;
    private static readonly EntityId _ship = ShipFixtures.ConstructId;
    private static readonly ShipStats _stats = FlightFixtures.Thirty();
    private static readonly PlanetId _home = FlightFixtures.Home.Id;

    private static (PlanetTerrainShipGate Gate, FakeTerrainReadiness Readiness, FlightRig.ListLogger Log) Create()
    {
        var readiness = new FakeTerrainReadiness();
        var log = new FlightRig.ListLogger();
        var world = ShipFixtures.CreateWorld(ShipFixtures.Thirty, default, ConstructMode.Ship);
        return (new PlanetTerrainShipGate(readiness, world, FlightRules.Default, 300f, TickRate, log), readiness, log);
    }

    private static ShipMotion At(double heightOverSurface, Vector3 velocity = default) => new()
    {
        Pose = new PoseD(new Vector3D(0, FlightFixtures.Home.RadiusMeters + heightOverSurface, 0), QuaternionD.Identity),
        LinearVelocity = velocity,
        Flags = ShipFlightFlags.Assist,
    };

    private static ShipTerrainGate Query(PlanetTerrainShipGate gate, in ShipMotion motion) => gate.Query(_ship, _home, motion, _stats, out _);

    [Fact]
    public void ShipTerrainGate_ColumnGeometry_QueriesGroundUnderShip()
    {
        var (gate, readiness, _) = Create();
        var motion = At(3000);

        Assert.Equal(ShipTerrainGate.Ready, gate.Query(_ship, _home, motion, _stats, out var cap));

        var center = readiness.LastCenter;
        var shipDistance = FlightFixtures.Home.RadiusMeters + 3000;
        Assert.True(center.Length() < shipDistance, $"the column centre {center} is under the ship");
        Assert.True(center.Length() >= FlightFixtures.Home.RadiusMeters, "and not under the highest ground");
        Assert.InRange(Vector3.Dot(Vector3.Normalize(center), Vector3.UnitY), 0.999f, 1.001f);
        Assert.Equal(double.PositiveInfinity, cap);
        Assert.False(gate.LastQuery.HasSweep);

        // Near the ground the sweep box is asked too and the horizontal speed is capped.
        Assert.Equal(ShipTerrainGate.Ready, gate.Query(_ship, _home, At(320, new Vector3(30, 0, 0)), _stats, out var low));
        Assert.True(gate.LastQuery.HasSweep);
        Assert.Equal(TerrainSweep.MaxHorizontalSpeed(_stats.BoundingRadius), (float)low);
    }

    [Fact]
    public void ShipTerrainGate_TimeoutCountsOnlyTicksThatHoldTheShip()
    {
        var (gate, readiness, _) = Create();
        readiness.Answer = PlanetTerrainReadiness.Streaming;
        var timeout = (int)(FlightRules.Default.ReadinessTimeoutSeconds * TickRate);
        var high = At(2 * FlightFixtures.Home.RadiusMeters);

        // Above 2 R a terrain that is not loaded does not hold the ship: no timeout however long it streams.
        for (var i = 0; i < timeout + 10; i++)
        {
            Assert.Equal(ShipTerrainGate.NotLoaded, Query(gate, high));
        }

        // Low, where the gate holds it, the timeout counts from the first holding tick.
        for (var i = 1; i < timeout; i++)
        {
            Assert.Equal(ShipTerrainGate.CollisionPending, Query(gate, At(350)));
        }

        Assert.Equal(ShipTerrainGate.Unavailable, Query(gate, At(350)));
    }

    [Fact]
    public void ShipTerrainGate_StreamingBeyondTimeout_Unavailable()
    {
        var (gate, readiness, _) = Create();
        readiness.Answer = PlanetTerrainReadiness.Streaming;
        var timeout = (int)(FlightRules.Default.ReadinessTimeoutSeconds * TickRate);

        // High above the ground a streaming terrain is "not loaded"; low it is "collision pending" (hover).
        // (above 2 R the gate does not hold the ship, so that query does not count toward the timeout).
        Assert.Equal(ShipTerrainGate.NotLoaded, Query(gate, At(4000)));
        for (var i = 1; i < timeout; i++)
        {
            Assert.Equal(ShipTerrainGate.CollisionPending, Query(gate, At(350)));
        }

        Assert.Equal(ShipTerrainGate.Unavailable, Query(gate, At(350)));
        readiness.Answer = PlanetTerrainReadiness.Inactive;
        var (other, inactive, _) = Create();
        inactive.Answer = PlanetTerrainReadiness.Inactive;
        Assert.Equal(ShipTerrainGate.NotLoaded, Query(other, At(350)));
    }

    [Fact]
    public void ShipTerrainGate_Faulted_UnavailableImmediately()
    {
        var (gate, readiness, _) = Create();
        readiness.Answer = PlanetTerrainReadiness.Faulted;

        Assert.Equal(ShipTerrainGate.Unavailable, Query(gate, At(5000)));
    }

    [Fact]
    public void ShipTerrainGate_ReadyAgain_Recovers()
    {
        var (gate, readiness, _) = Create();
        readiness.Answer = PlanetTerrainReadiness.Faulted;
        Assert.Equal(ShipTerrainGate.Unavailable, Query(gate, At(350)));
        var asked = gate.Queries;

        readiness.Answer = PlanetTerrainReadiness.Ready;
        var ticks = 0;
        while (Query(gate, At(350)) == ShipTerrainGate.Unavailable)
        {
            ticks++;
            Assert.True(ticks <= PlanetTerrainShipGate.RetrySeconds * TickRate, "retried within 5 s");
        }

        Assert.Equal(asked + 1, gate.Queries);
        Assert.Equal(ShipTerrainGate.Ready, Query(gate, At(350)));

        // Parking resets the wait.
        readiness.Answer = PlanetTerrainReadiness.Faulted;
        Assert.Equal(ShipTerrainGate.Unavailable, Query(gate, At(350)));
        readiness.Answer = PlanetTerrainReadiness.Ready;
        Assert.Equal(ShipTerrainGate.Ready, Query(gate, At(350) with { Flags = ShipFlightFlags.Parked }));
    }

    [Fact]
    public void ShipTerrainGate_Unavailable_LogsAtMostEvery10s()
    {
        var (gate, readiness, log) = Create();
        readiness.Answer = PlanetTerrainReadiness.Faulted;

        for (var i = 0; i < 30 * TickRate; i++)
        {
            Assert.Equal(ShipTerrainGate.Unavailable, Query(gate, At(350)));
        }

        Assert.Equal(3, gate.Warnings);
        Assert.Equal(3, log.Lines.Count(l => l.StartsWith("Warning:2401", StringComparison.Ordinal)));
    }

    [Fact]
    public void UnparkedShipDemand_DemandsOnlyUnparkedShips()
    {
        var world = ShipFixtures.CreateWorld(ShipFixtures.Thirty, default, ConstructMode.Ship);
        var sim = new Core.Simulation.ServerSimulation(
            world, new Testing.FakeTerrainService(), new Testing.InMemoryJournal(), new Core.Simulation.GameRules(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.Simulation.ServerSimulation>.Instance, ShipFixtures.ContentFor(world));
        world.Constructs.TryGet(_ship, out var ship);
        var demand = new UnparkedShipDemand(world);

        Assert.False(demand.Demands(ship!));
        Assert.True(sim.UpdateShipState(_ship, new ShipStateRecord(_ship, 10f, default, default, ShipStateFlags.Assist)));
        Assert.True(demand.Demands(ship!));
        Assert.True(sim.UpdateShipState(_ship, new ShipStateRecord(_ship, 10f, default, default, ShipStateFlags.Parked)));
        Assert.False(demand.Demands(ship!));
    }
}
