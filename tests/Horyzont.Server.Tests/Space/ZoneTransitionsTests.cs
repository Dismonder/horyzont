using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Space;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Space;
using Horyzont.Server.Tests.Frames;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Space;

public sealed class ZoneTransitionsTests
{
    private const ulong Seed = 21;
    private const double MaxPoseError = 1e-3;
    private static readonly EntityId _ship = new(10);
    private static readonly FrameRef _system = FrameRef.Of(new SystemId(1));

    [Fact]
    public void ZoneTransitions_ShipInSystemReaches3_0R_ReparentsToPlanet()
    {
        var moon = Moon();
        var rig = Rig(new TestShip(_ship, _system, ToFloat(moon.Position + (Outward * (3.0 * moon.RadiusMeters + 5))), Quaternion.Identity));
        var zones = Zones(rig);

        Step(rig, zones, 5);
        Assert.Empty(Reparents(rig));

        MoveTo(rig, _ship, moon.Position + (Outward * (3.0 * moon.RadiusMeters - 1)));
        var before = rig.WorldPose(_ship);
        Step(rig, zones);

        var op = Assert.Single(Reparents(rig));
        Assert.Equal(FrameRef.Of(moon.Planet), op.NewParent);
        Assert.Equal(FrameRef.Of(moon.Planet), Construct(rig).Parent);
        Assert.InRange(Vector3D.Distance(before.Position, rig.WorldPose(_ship).Position), 0, MaxPoseError);
        Assert.Equal(1, zones.Reparented);
    }

    [Fact]
    public void ZoneTransitions_ShipInPlanetBeyond3_2R_ReparentsToSystem()
    {
        var home = Home();
        var rig = Rig(new TestShip(_ship, FrameRef.Of(home.Planet), ToFloat(Outward * (3.1 * home.RadiusMeters)), Quaternion.Identity));
        var zones = Zones(rig);

        Step(rig, zones, 5);
        Assert.Empty(Reparents(rig));

        MoveTo(rig, _ship, Outward * (3.2 * home.RadiusMeters + 1));
        var before = rig.WorldPose(_ship);
        Step(rig, zones);

        var op = Assert.Single(Reparents(rig));
        Assert.Equal(_system, op.NewParent);
        Assert.Equal(_system, Construct(rig).Parent);
        Assert.InRange(Vector3D.Distance(before.Position, rig.WorldPose(_ship).Position), 0, MaxPoseError);
    }

    [Fact]
    public void ZoneTransitions_ShipOscillatingInBand_NoOperations()
    {
        var moon = Moon();
        var rig = Rig(new TestShip(_ship, _system, ToFloat(moon.Position + (Outward * (3.1 * moon.RadiusMeters))), Quaternion.Identity));
        var zones = Zones(rig);

        // Enters at 3.0 R, then swings between 2.95 R and 3.18 R: one reparent, no flapping.
        MoveTo(rig, _ship, moon.Position + (Outward * (2.99 * moon.RadiusMeters)));
        Step(rig, zones);
        Assert.Single(Reparents(rig));
        for (var i = 0; i < 200; i++)
        {
            var factor = 3.065 + (0.115 * Math.Sin(i * 0.3));
            MoveTo(rig, _ship, Outward * (factor * moon.RadiusMeters));
            Step(rig, zones);
        }

        Assert.Single(Reparents(rig));
        Assert.Equal(FrameRef.Of(moon.Planet), Construct(rig).Parent);
    }

    [Fact]
    public void ZoneTransitions_PassengersNotTouched()
    {
        var home = Home();
        var rig = Rig(new TestShip(_ship, FrameRef.Of(home.Planet), new Vector3(0f, (float)home.RadiusMeters + 10f, 0f), Quaternion.Identity));
        var zones = Zones(rig);
        var player = rig.Join();
        Step(rig, zones, 3);
        Assert.True(rig.Transitions.TryReparent(player.Entity, FrameRef.Of(_ship), new CellPos(1, 0, 1), out _, out _));
        var seq = player.ParentSeq;

        for (var i = 1; i <= 10; i++)
        {
            MoveTo(rig, _ship, Outward * (home.RadiusMeters + 10 + (i * 0.33 * home.RadiusMeters)));
            Step(rig, zones);
        }

        var shipOps = Reparents(rig).Where(op => op.Entity == _ship).ToList();
        Assert.Single(shipOps);
        Assert.Equal(_system, Construct(rig).Parent);
        Assert.Equal(FrameRef.Of(_ship), player.Frame);
        Assert.Equal(seq, player.ParentSeq);
        Assert.Single(Reparents(rig), op => op.Entity == player.Entity);
    }

    [Fact]
    public void ZoneTransitions_StaticConstructIgnored()
    {
        var home = Home();
        var rig = Rig(new TestShip(
            _ship, FrameRef.Of(home.Planet), ToFloat(Outward * (3.5 * home.RadiusMeters)), Quaternion.Identity, ConstructMode.Static));
        var zones = Zones(rig);

        Step(rig, zones, 10);

        Assert.Empty(Reparents(rig));
        Assert.Equal(FrameRef.Of(home.Planet), Construct(rig).Parent);
    }

    [Fact]
    public void ZoneTransitions_WorldWithoutStarSystem_DoesNothing()
    {
        var rig = new FrameRig(TestShips.CreateWorld(new TestShip(_ship, TestShips.HomeFrame, new Vector3(0f, 20_000f, 0f), Quaternion.Identity)));
        var zones = Zones(rig);

        Step(rig, zones, 5);

        Assert.Empty(Reparents(rig));
    }

    [Fact]
    public void ZoneTransitions_Step_EightShipsAllocatesNothing()
    {
        var home = Home();
        var moon = Moon();
        var ships = new List<TestShip>();
        for (var i = 0; i < 8; i++)
        {
            var id = new EntityId(10 + (ulong)i);
            ships.Add(i % 2 == 0
                ? new TestShip(id, _system, ToFloat(moon.Position + (Outward * ((3.1 * moon.RadiusMeters) + (i * 3)))), Quaternion.Identity)
                : new TestShip(id, FrameRef.Of(home.Planet), ToFloat(Outward * ((1.5 * home.RadiusMeters) + (i * 3))), Quaternion.Identity));
        }

        var rig = Rig([.. ships]);
        var zones = Zones(rig);
        for (var i = 0; i < 50; i++)
        {
            zones.Step(rig.State);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            zones.Step(rig.State);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Empty(Reparents(rig));
    }

    private static Vector3D Outward => Vector3D.Normalize(new Vector3D(1, 2, 3));

    private static BodyInfo Home() => Model().GetBody(0);

    private static BodyInfo Moon() => Model().GetBody(1);

    private static StarSystemModel Model()
    {
        var created = StarSystemGenesis.CreateWorldBodies(Seed, "core:test/1");
        return new StarSystemModel(created.System, created.Bodies);
    }

    private static FrameRig Rig(params TestShip[] ships) => new(TestShips.CreateStarSystemWorld(Seed, ships));

    private static ServerZoneTransitions Zones(FrameRig rig) =>
        new(() => rig.Transitions, rig.Sim.Rules.TickRate, NullLogger<ServerZoneTransitions>.Instance);

    private static Construct Construct(FrameRig rig) =>
        rig.State.Constructs.TryGet(_ship, out var construct) ? construct : throw new InvalidOperationException();

    /// <summary>Ticks like the server: zones in phase Frames, the history in Compose, then the tick advances.</summary>
    private static void Step(FrameRig rig, ServerZoneTransitions zones, int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            zones.Step(rig.State);
            rig.History.Step(rig.State.CurrentTick);
            rig.Sim.AdvanceTick();
        }
    }

    /// <summary>Moves a ship to a point of its current parent frame (planet or system frame).</summary>
    private static void MoveTo(FrameRig rig, EntityId ship, Vector3D local)
    {
        var construct = rig.State.Constructs.TryGet(ship, out var c) ? c : throw new InvalidOperationException();
        Assert.True(rig.Sim.UpdateConstructKinematics(ship, construct.Parent, construct.ParentSeq, ToFloat(local), construct.Rotation));
    }

    private static List<EntityReparentOp> Reparents(FrameRig rig) =>
        rig.Journal.Entries.Select(e => e.Operation).OfType<EntityReparentOp>().ToList();

    private static Vector3 ToFloat(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
