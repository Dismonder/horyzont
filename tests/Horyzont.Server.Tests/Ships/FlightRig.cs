using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;
using Horyzont.Server.Ships;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Ships;

/// <summary>The server flight over the 30-block ship on the home planet with a sphere ground (K5-24 server tests).</summary>
internal sealed class FlightRig
{
    public const int TickRate = 60;

    public FlightRig(double heightOverGround = 0, IShipTerrainGate? gate = null, IShipControlSource? control = null, int ships = 1, ShipStateRecord? state = null)
    {
        var stats = FlightFixtures.Thirty();
        Radius = FlightFixtures.Home.RadiusMeters;
        var restY = Radius + stats.BoundingRadius + 0.01 - stats.CenterOfMass.Y;
        var origin = new Vector3(0, (float)(restY + heightOverGround), 0);
        var world = ShipFixtures.CreateWorld(ShipFixtures.Thirty, origin, ConstructMode.Ship);
        Sim = new ServerSimulation(world, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance, ShipFixtures.ContentFor(world));
        Ids.Add(ShipFixtures.ConstructId);
        for (var i = 1; i < ships; i++)
        {
            var id = new EntityId(ShipFixtures.ConstructId.Value + (ulong)(i * 10));
            var at = Vector3.Normalize(new Vector3(i * 0.05f, 1, 0)) * origin.Y;
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.Atan2(at.X, at.Y));
            foreach (var op in ShipFixtures.BuildOperations(id, ShipFixtures.Thirty, at))
            {
                Sim.ApplyServerOperation(op is ConstructCreateOp create ? create with { Rotation = rotation } : op);
            }

            Sim.State.Constructs.TryGet(id, out var c);
            Sim.ApplyServerOperation(new ShipModeSetOp(id, ConstructMode.Ship, c!.Parent, c.ParentSeq, c.Origin, c.Rotation));
            Ids.Add(id);
        }

        if (state is { } record)
        {
            Assert.True(Sim.UpdateShipState(ShipFixtures.ConstructId, record));
        }

        Stats = new ShipStatsCache(Sim);
        Ground = new SphereGroundCollider().Add(Vector3D.Zero, Radius);
        Collider = new SphereShipCollider(Ground, Stats);
        Logger = new ListLogger();
        Gate = gate ?? new HomeOnlyGate(FlightFixtures.Home.Id);
        Flight = new ShipFlightSystem(Sim, Stats, control ?? NoPilotControlSource.Instance, Collider, Gate, FlightRules.Default, TickRate, Logger);
    }

    public ServerSimulation Sim { get; }

    public ShipStatsCache Stats { get; }

    public SphereGroundCollider Ground { get; }

    public SphereShipCollider Collider { get; }

    public IShipTerrainGate Gate { get; }

    public ShipFlightSystem Flight { get; }

    public ListLogger Logger { get; }

    public double Radius { get; }

    public List<EntityId> Ids { get; } = [];

    public static EntityId Ship => ShipFixtures.ConstructId;

    public Construct Construct(EntityId? id = null)
    {
        Assert.True(Sim.State.Constructs.TryGet(id ?? Ship, out var c));
        return c!;
    }

    public ShipStateRecord Record(EntityId? id = null)
    {
        Assert.True(Sim.State.ShipStates.TryGet(id ?? Ship, out var record));
        return record;
    }

    /// <summary>Distance of the centre of mass of the ship from the planet centre.</summary>
    public double ComDistance(EntityId? id = null)
    {
        var c = Construct(id);
        Assert.True(Stats.TryGet(c.Id, out var stats));
        return c.LocalToParent(stats.CenterOfMass).Length();
    }

    /// <summary>Distance of the centre of mass over the resting distance (ground contact).</summary>
    public double HeightOverGround(EntityId? id = null)
    {
        Assert.True(Stats.TryGet(id ?? Ship, out var stats));
        return ComDistance(id) - (Radius + stats.BoundingRadius);
    }

    public void Ticks(int count)
    {
        for (var i = 0; i < count; i++)
        {
            Flight.Step();
        }
    }

    internal sealed class ListLogger : ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            Lines.Add($"{logLevel}:{eventId.Id}:{formatter(state, exception)}");
        }
    }
}
