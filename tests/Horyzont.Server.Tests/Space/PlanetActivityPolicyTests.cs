using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Space;
using Horyzont.Server.Tests.Frames;
using Horyzont.Testing.Ships;

namespace Horyzont.Server.Tests.Space;

public sealed class PlanetActivityPolicyTests
{
    private const ulong Seed = 21;
    private static readonly EntityId _ship = new(10);

    [Fact]
    public void PlanetActivityPolicy_PlayerInRegion_Activates()
    {
        var rig = new FrameRig(TestShips.CreateStarSystemWorld(Seed));
        var policy = new PlanetActivityPolicy();
        var moon = Moon(rig);
        var player = rig.Join();

        Assert.True(Step(rig, policy, NoShips.Instance));
        Assert.Equal([rig.State.HomePlanet], policy.Activate);
        Assert.False(policy.IsActive(moon));

        Assert.True(rig.Transitions.TryReparent(player.Entity, FrameRef.Of(moon), null, out _, out _));
        var activations = Run(rig, policy, NoShips.Instance, 30);

        Assert.Equal([moon], activations.Activated);
        Assert.True(policy.IsActive(moon));
        Assert.True(policy.IsActive(rig.State.HomePlanet));
    }

    [Fact]
    public void PlanetActivityPolicy_LastPlayerLeaves_DeactivatesAfter60s()
    {
        var rig = new FrameRig(TestShips.CreateStarSystemWorld(Seed));
        var policy = new PlanetActivityPolicy();
        var moon = Moon(rig);
        var player = rig.Join();
        Assert.True(rig.Transitions.TryReparent(player.Entity, FrameRef.Of(moon), null, out _, out _));
        Run(rig, policy, NoShips.Instance, 31);
        Assert.True(policy.IsActive(moon));

        rig.Sim.ApplyServerOperation(new PlayerLeaveOp(player.Player, player.Frame, player.ParentSeq, player.Position, player.BodyRotation, 0f));
        var left = rig.State.CurrentTick.Value;
        var run = Run(rig, policy, NoShips.Instance, 3700);

        Assert.Equal([moon], run.Deactivated);
        var after = run.DeactivatedAt - left;
        Assert.InRange(after, 3600UL - 30UL, 3600UL + 30UL);
        Assert.False(policy.IsActive(moon));
        Assert.True(policy.IsActive(rig.State.HomePlanet));
    }

    [Fact]
    public void PlanetActivityPolicy_ReturnsWithinLinger_StaysActive()
    {
        var rig = new FrameRig(TestShips.CreateStarSystemWorld(Seed));
        var policy = new PlanetActivityPolicy();
        var moon = Moon(rig);
        var player = rig.Join();
        Assert.True(rig.Transitions.TryReparent(player.Entity, FrameRef.Of(moon), null, out _, out _));
        Run(rig, policy, NoShips.Instance, 31);

        Assert.True(rig.Transitions.TryReparent(player.Entity, FrameRef.Of(rig.State.HomePlanet), null, out _, out _));
        var away = Run(rig, policy, NoShips.Instance, 3000);
        Assert.True(rig.Transitions.TryReparent(player.Entity, FrameRef.Of(moon), null, out _, out _));
        var back = Run(rig, policy, NoShips.Instance, 3000);

        Assert.Empty(away.Deactivated);
        Assert.Empty(back.Deactivated);
        Assert.Empty(back.Activated);
        Assert.True(policy.IsActive(moon));
    }

    [Fact]
    public void PlanetActivityPolicy_LeaseKeepsActive()
    {
        var rig = new FrameRig(TestShips.CreateStarSystemWorld(Seed));
        var policy = new PlanetActivityPolicy();
        var moon = Moon(rig);
        Step(rig, policy, NoShips.Instance);

        Assert.True(policy.AddLease(rig.State, moon));
        Assert.False(policy.AddLease(rig.State, moon));
        var held = Run(rig, policy, NoShips.Instance, 5000);
        Assert.Empty(held.Deactivated);
        Assert.Equal(2, policy.LeaseCount(moon));

        policy.ReleaseLease(moon, rig.State.CurrentTick);
        policy.ReleaseLease(moon, rig.State.CurrentTick);
        var released = rig.State.CurrentTick.Value;
        var run = Run(rig, policy, NoShips.Instance, 3700);

        Assert.Equal([moon], run.Deactivated);
        Assert.InRange(run.DeactivatedAt - released, 3600UL, 3600UL + 30UL);
        Assert.Throws<InvalidOperationException>(() => policy.ReleaseLease(moon, rig.State.CurrentTick));
    }

    [Fact]
    public void PlanetActivityPolicy_MovingShipDemands_ParkedDoesNot()
    {
        var moon = Moon(new FrameRig(TestShips.CreateStarSystemWorld(Seed)));
        var parkedRig = new FrameRig(TestShips.CreateStarSystemWorld(Seed, new TestShip(_ship, FrameRef.Of(moon), new Vector3(0f, 5000f, 0f), Quaternion.Identity)));
        var parked = new PlanetActivityPolicy();
        Run(parkedRig, parked, new MovingShipDemand(parkedRig.History), 100);
        Assert.False(parked.IsActive(moon));

        var movingRig = new FrameRig(TestShips.CreateStarSystemWorld(Seed, new TestShip(_ship, FrameRef.Of(moon), new Vector3(0f, 5000f, 0f), Quaternion.Identity)));
        var moving = new PlanetActivityPolicy();
        var y = 5000f;
        var activated = false;
        for (var i = 0; i < 100; i++)
        {
            y -= 1f;
            Assert.True(movingRig.State.Constructs.TryGet(_ship, out var ship));
            movingRig.Sim.UpdateConstructKinematics(_ship, ship.Parent, ship.ParentSeq, new Vector3(0f, y, 0f), Quaternion.Identity);
            movingRig.History.Step(movingRig.State.CurrentTick);
            moving.Update(movingRig.State.CurrentTick, movingRig.State, movingRig.Graph, new MovingShipDemand(movingRig.History));
            activated |= moving.Activate.Contains(moon);
            movingRig.Sim.AdvanceTick();
        }

        Assert.True(activated);
        Assert.True(moving.IsActive(moon));
    }

    [Fact]
    public void PlanetActivityPolicy_ShipDemandSource_Replaceable()
    {
        var moon = Moon(new FrameRig(TestShips.CreateStarSystemWorld(Seed)));
        var rig = new FrameRig(TestShips.CreateStarSystemWorld(Seed, new TestShip(_ship, FrameRef.Of(moon), new Vector3(0f, 5000f, 0f), Quaternion.Identity)));
        var policy = new PlanetActivityPolicy();

        // A ship standing still but "not parked" (K5-24 UnparkedShipDemand) demands its planet.
        var run = Run(rig, policy, new AllShips(), 31);

        Assert.Contains(moon, run.Activated);
        Assert.True(policy.IsActive(moon));
    }

    [Fact]
    public void PlanetActivityPolicy_PassengerUsesShipRegion()
    {
        var probe = new FrameRig(TestShips.CreateStarSystemWorld(Seed));
        var moon = Moon(probe);
        var rig = new FrameRig(TestShips.CreateStarSystemWorld(Seed, new TestShip(_ship, FrameRef.Of(moon), new Vector3(0f, 5000f, 0f), Quaternion.Identity)));
        var policy = new PlanetActivityPolicy();
        var player = rig.Join();
        Assert.True(rig.Transitions.TryReparent(player.Entity, FrameRef.Of(_ship), new CellPos(1, 0, 1), out _, out _));

        var run = Run(rig, policy, NoShips.Instance, 31);

        Assert.Equal(FrameRef.Of(_ship), player.Frame);
        Assert.Contains(moon, run.Activated);
    }

    [Fact]
    public void PlanetActivityPolicy_HomeAlwaysActive()
    {
        var rig = new FrameRig(TestShips.CreateStarSystemWorld(Seed));
        var policy = new PlanetActivityPolicy();

        var run = Run(rig, policy, NoShips.Instance, 10_000);

        Assert.Equal([rig.State.HomePlanet], run.Activated);
        Assert.Empty(run.Deactivated);
        Assert.True(policy.IsActive(rig.State.HomePlanet));
    }

    [Fact]
    public void PlanetActivityPolicy_OutputSortedById()
    {
        var probe = new FrameRig(TestShips.CreateStarSystemWorld(Seed));
        var planets = probe.State.Planets.All.Select(p => p.Id).ToList();
        Assert.Equal(4, planets.Count);

        // Ship ids in reverse planet order: the constructs are visited by entity id, the output still comes sorted by planet id.
        var ships = new TestShip[planets.Count];
        for (var i = 0; i < planets.Count; i++)
        {
            ships[i] = new TestShip(new EntityId((ulong)(50 - i)), FrameRef.Of(planets[i]), new Vector3(0f, 9000f, 0f), Quaternion.Identity);
        }

        var rig = new FrameRig(TestShips.CreateStarSystemWorld(Seed, ships));
        var policy = new PlanetActivityPolicy();
        var demand = new SwitchShips { On = true };

        Assert.True(Step(rig, policy, demand));
        Assert.Equal(planets.OrderBy(p => p.Value).ToList(), policy.Activate);

        demand.On = false;
        var run = Run(rig, policy, demand, 3700);
        var others = planets.Where(p => p != rig.State.HomePlanet).OrderBy(p => p.Value).ToList();
        Assert.Equal(others, run.Deactivated);
    }

    [Fact]
    public void PlanetActivityPolicy_Update_AllocatesZeroBytes()
    {
        var ships = new TestShip[8];
        var moon = Moon(new FrameRig(TestShips.CreateStarSystemWorld(Seed)));
        for (var i = 0; i < ships.Length; i++)
        {
            ships[i] = new TestShip(new EntityId((ulong)(10 + i)), FrameRef.Of(moon), new Vector3(i * 20f, 5000f, 0f), Quaternion.Identity);
        }

        var rig = new FrameRig(TestShips.CreateStarSystemWorld(Seed, ships));
        for (var i = 0; i < 8; i++)
        {
            rig.Join((ulong)(i + 1));
        }

        var policy = new PlanetActivityPolicy(new PlanetActivityOptions { EvaluationIntervalTicks = 1 });
        var demand = new MovingShipDemand(rig.History);
        rig.History.Step(rig.State.CurrentTick);
        policy.Update(rig.State.CurrentTick, rig.State, rig.Graph, demand);

        var tick = rig.State.CurrentTick.Value;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 1; i <= 10_000; i++)
        {
            policy.Update(new Tick(tick + (ulong)i), rig.State, rig.Graph, demand);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static PlanetId Moon(FrameRig rig) =>
        rig.State.StarSystem is { } system ? system.Bodies[1].Planet : throw new InvalidOperationException("no star system");

    private static bool Step(FrameRig rig, PlanetActivityPolicy policy, IShipDemandSource ships)
    {
        rig.History.Step(rig.State.CurrentTick);
        var evaluated = policy.Update(rig.State.CurrentTick, rig.State, rig.Graph, ships);
        rig.Sim.AdvanceTick();
        return evaluated;
    }

    private static Changes Run(FrameRig rig, PlanetActivityPolicy policy, IShipDemandSource ships, int ticks)
    {
        var changes = new Changes();
        for (var i = 0; i < ticks; i++)
        {
            var tick = rig.State.CurrentTick.Value;
            Step(rig, policy, ships);
            changes.Activated.AddRange(policy.Activate);
            if (policy.Deactivate.Count > 0)
            {
                changes.Deactivated.AddRange(policy.Deactivate);
                changes.DeactivatedAt = tick;
            }
        }

        return changes;
    }

    private sealed class Changes
    {
        public List<PlanetId> Activated { get; } = [];

        public List<PlanetId> Deactivated { get; } = [];

        public ulong DeactivatedAt { get; set; }
    }

    private sealed class NoShips : IShipDemandSource
    {
        public static readonly NoShips Instance = new();

        public bool Demands(Construct ship) => false;
    }

    private sealed class SwitchShips : IShipDemandSource
    {
        public bool On { get; set; }

        public bool Demands(Construct ship) => On;
    }

    private sealed class AllShips : IShipDemandSource
    {
        public bool Demands(Construct ship) => ship.Mode == ConstructMode.Ship;
    }
}
