using Godot;

using Horyzont.Core.Frames;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Frames;
using Horyzont.Game.Physics;
using Horyzont.Game.Ships;
using Horyzont.Game.Testing;
using Horyzont.Game.Tests.Constructs;
using Horyzont.Server.Ships;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Game.Tests.Ships;

/// <summary>K5-24 decision 9: the engine sweep of a ship hull proxy.</summary>
public sealed class GodotShipColliderTests : EngineTestBase
{
    private const float Height = 4000f;

    [EngineTest]
    public async Task GodotShipCollider_HitsTerrainAndConstructs_ExcludesOwnChunks()
    {
        using var rig = await Rig.Create(this);
        var start = Pose(0, Height, 0);

        // A wall construct 10 m beyond the hull (cells 0 … 4 along X): a 20 m sweep stops near half way, the normal faces back.
        var wall = rig.Sweep(start, Pose(20, Height, 0));
        EngineAssert.True(wall.Hit, "the wall construct is hit");
        EngineAssert.True(wall.Fraction is > 0.45 and < 0.52, $"safe fraction {wall.Fraction:F3} ≈ 0.5");
        EngineAssert.True(wall.Normal.X < -0.9, $"normal {wall.Normal} faces −X");

        // The ground body (terrain layer) 5 m under the hull.
        var ground = rig.Sweep(start, Pose(0, Height - 20, 0));
        EngineAssert.True(ground.Hit, "the terrain layer is hit");
        EngineAssert.True(ground.Normal.Y > 0.9, $"normal {ground.Normal} faces up");

        // The ship's own chunk bodies overlap the proxy at every pose: they must not stop it.
        var own = rig.Sweep(start, Pose(0.5, Height + 0.5, 0));
        EngineAssert.True(!own.Hit, "own chunks are excluded");
        EngineAssert.Equal(1, rig.Collider.ProxyCount, "one proxy body per ship");
        rig.Collider.Forget(ShipFixtures.ConstructId);
        EngineAssert.Equal(0, rig.Collider.ProxyCount, "forget frees the proxy");
    }

    [EngineTest]
    public async Task GodotShipCollider_HitStopsWithoutBounce_BlocksUnchanged()
    {
        using var rig = await Rig.Create(this);
        var hash = StateHasher.Hash(rig.World.State);
        var rules = new FlightRules();
        var from = Pose(0, Height, 0);
        var motion = new ShipMotion { Pose = Pose(20, Height, 0), LinearVelocity = new System.Numerics.Vector3(30, 0, 1), Flags = ShipFlightFlags.Assist };
        var environment = new FlightEnvironment { Up = new Vector3D(0, 1, 0) };

        var hit = rig.Sweep(from, motion.Pose);
        var parked = ShipContact.Resolve(ref motion, from, hit, environment, rules);

        EngineAssert.True(hit.Hit && !parked, "a wall hit stops the ship without parking it");
        EngineAssert.True(motion.Pose.Position.X is > 0 and < 11, $"stopped before the wall at x = {motion.Pose.Position.X:F2}");
        EngineAssert.True(Math.Abs(motion.LinearVelocity.X) < 1e-3, $"no velocity into the wall and no bounce ({motion.LinearVelocity.X})");
        EngineAssert.True(motion.LinearVelocity.Z > 0, "the tangential velocity is kept (with friction)");
        EngineAssert.Equal(hash, StateHasher.Hash(rig.World.State), "no block destroyed or changed");
    }

    [EngineTest]
    public async Task GodotShipCollider_YawIntoWallThenFlyAway_NotStuck()
    {
        using var rig = await Rig.Create(this);
        var rules = new FlightRules();
        var environment = new FlightEnvironment { Up = new Vector3D(0, 1, 0) };

        // The hull (x 9 … 14) is 1 m from the wall at x = 15; a 45° yaw swings its far corner about 2 m into the wall.
        var from = Pose(9, Height, 0);
        var motion = new ShipMotion
        {
            Pose = new PoseD(from.Position, QuaternionD.FromAxisAngle(new Vector3D(0, 1, 0), Math.PI / 4)),
            Flags = ShipFlightFlags.Assist,
        };
        var yaw = rig.Sweep(from, motion.Pose);
        EngineAssert.True(yaw.Hit, "yawing the hull into the wall hits it");
        ShipContact.Resolve(ref motion, from, yaw, environment, rules);

        // Flying away along −X must move the ship: 10 steps of 0.5 m.
        for (var i = 0; i < 10; i++)
        {
            var start = motion.Pose;
            motion.Pose = new PoseD(start.Position + new Vector3D(-0.5, 0, 0), start.Rotation);
            motion.LinearVelocity = new System.Numerics.Vector3(-30, 0, 0);
            ShipContact.Resolve(ref motion, start, rig.Sweep(start, motion.Pose), environment, rules);
        }

        EngineAssert.True(motion.Pose.Position.X < 5, $"the ship flew away from the wall (x = {motion.Pose.Position.X:F2})");
        var free = rig.Sweep(motion.Pose, new PoseD(motion.Pose.Position + new Vector3D(-0.5, 0, 0), motion.Pose.Rotation));
        EngineAssert.True(!free.Hit, "the ship is clear of the wall");
    }

    private static PoseD Pose(double x, double y, double z) => new(new Vector3D(x, y, z), QuaternionD.Identity);

    private sealed class Rig : IDisposable
    {
        private StaticBody3D? _ground;

        private Rig(ConstructTestWorld world, GodotShipCollider collider)
        {
            World = world;
            Collider = collider;
        }

        public ConstructTestWorld World { get; }

        public GodotShipCollider Collider { get; }

        /// <summary>The 30-block ship (ship mode) at (0, 4000, 0), a stone wall 10 m beyond it and a ground body 5 m under it.</summary>
        public static async Task<Rig> Create(GodotShipColliderTests test)
        {
            var state = ShipFixtures.CreateWorld(ShipFixtures.Thirty, new System.Numerics.Vector3(0, Height, 0), ConstructMode.Ship);
            var world = new ConstructTestWorld(test.Host, state);
            for (var y = -1; y < 4; y++)
            {
                for (var z = -2; z < 7; z++)
                {
                    world.Create(new CellPos(0, y, z), origin: new System.Numerics.Vector3(15, Height, 0));
                }
            }

            var statsSim = new ServerSimulation(
                state, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance, ShipFixtures.ContentFor(state));
            var collider = new GodotShipCollider(
                state,
                new ShipStatsCache(statsSim),
                frame => frame.Kind == FrameKind.Planet ? new PlanetSpace(new PlanetId(frame.Id), test.Host) : null,
                (ship, into) => GodotShipCollider.AddChunkBodies(world.World, ship, into));
            var rig = new Rig(world, collider);
            rig._ground = new StaticBody3D { Name = "Ground", CollisionLayer = CollisionLayers.TerrainMask, CollisionMask = 0 };
            rig._ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Godot.Vector3(200, 2, 200) } });
            test.Host.AddChild(rig._ground);
            rig._ground.GlobalPosition = new Godot.Vector3(0, Height - 6, 0);
            world.World.RebuildAll(ConstructTestWorld.Planet);
            await test.WaitForCollision(world);
            await test.PhysicsFrames(2);
            return rig;
        }

        public ShipSweepResult Sweep(in PoseD from, in PoseD to) => Collider.Sweep(ShipFixtures.ConstructId, from, to);

        public void Dispose()
        {
            Collider.Dispose();
            _ground?.QueueFree();
            World.Dispose();
        }
    }

    private Task WaitForCollision(ConstructTestWorld w) => WaitUntil(
        () => w.World.PendingJobs == 0
            && w.World.AreCollisionsReady(ConstructTestWorld.Planet, new System.Numerics.Vector3(2, Height + 2, 2), 20),
        600,
        "the ship and wall collision");
}
