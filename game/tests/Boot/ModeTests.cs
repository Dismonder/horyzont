using GdUnit4;

using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Game.Tests.Support;
using Horyzont.Server;

using static GdUnit4.Assertions;

using Quaternion = System.Numerics.Quaternion;
using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Boot;

[TestSuite]
public class ModeTests
{
    [TestCase]
    [RequireGodotRuntime]
    public async Task Await_AfterWorkerTask_ContinuesOnMainThread()
    {
        // R3: ShutdownSequence and the start code rely on continuations running on the Godot main thread.
        var mainThread = OS.GetMainThreadId();
        AssertThat(OS.GetThreadCallerId()).IsEqual(mainThread);
        await Task.Run(() => System.Threading.Thread.Sleep(20));
        AssertThat(OS.GetThreadCallerId()).IsEqual(mainThread);
        await Task.Delay(20);
        AssertThat(OS.GetThreadCallerId()).IsEqual(mainThread);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Threading_AllSimulationCallsOnMainThread()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var fixture = await ModeFixture.StartAsync(
            scene, dir, true, "--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString());
        AssertThat(await fixture.WaitInGame()).IsTrue();
        var composition = fixture.Composition;
        var server = composition.Server!;
        var planet = ModeFixture.Home(server.State);
        var surface = ModeFixture.SurfaceY(planet);
        var feet = new Vector3(0, surface + 0.05f, 0);
        AssertThat(server.Simulation.UpdatePlayerKinematics(composition.Client!.Hello!.YourPlayer, FrameRef.Of(planet.Id), 0, feet, Quaternion.Identity, 0f)).IsTrue();
        var service = composition.TerrainFactory!.Terrain!.Voxel;
        AssertThat(service.TryGetPlanet(planet.Id, out var node)).IsTrue();
        node!.AddChild(new VoxelViewer { Position = new Godot.Vector3(0, surface, 0), ViewDistance = 128, RequiresVisuals = false, RequiresCollisions = false });
        AssertThat(await fixture.WaitUntil(() => service.IsAreaLoaded(planet.Id, new Vector3(0, surface, 0), 12f))).IsTrue();

        // 300 ticks with a terrain edit every 20: the guard of VoxelTerrainService throws on any other thread and faults the simulation.
        var replies = new ModeFixture.Replies();
        composition.Client.AddListener(replies);
        var eye = feet + (Vector3.UnitY * server.Simulation.Rules.EyeHeight);
        var startTick = server.State.CurrentTick.Value;
        var sent = 0;
        while (server.State.CurrentTick.Value - startTick < 300)
        {
            if (server.State.CurrentTick.Value - startTick >= (ulong)(sent * 20))
            {
                composition.Client.Session.SubmitIntent(new TerrainToolIntent(planet.Id, eye, -Vector3.UnitY, 2f, sent % 2 == 0 ? TerrainEditMode.Remove : TerrainEditMode.Add, sent % 2 == 0 ? (byte)0 : (byte)1));
                sent++;
            }

            await scene.WaitFrames(1);
        }

        AssertThat(await fixture.WaitUntil(() => replies.All.Count == sent)).IsTrue();
        AssertThat(server.Simulation.IsFaulted).IsFalse();
        AssertThat(server.RuntimeState).IsEqual(ServerRuntimeState.Running);
        AssertThat(replies.All.Any(r => r.Reason == RejectReason.None)).IsTrue();
        AssertThat(await fixture.StopAsync()).IsTrue();
        AssertThat(composition.ExitCode).IsEqual(0);
    }

    [TestCase(Timeout = 120000)]
    [RequireGodotRuntime]
    public async Task HeadlessServerTerrain_GeneratesCollisionsWithoutRenderer()
    {
        // R1: a dedicated server has no renderer; its terrain must still produce collisions and accept edits.
        await using var scene = EngineTestScene.Create();
        var root = scene.Add(new Node3D { Name = "R1" });
        var planet = Horyzont.Core.World.PlanetGenesis.CreatePlanet(1, Horyzont.Core.World.PlanetGenesis.HomePlanetIndex, Horyzont.Game.Planet.Generators.GeneratorProfiles.Current);
        var terrain = Horyzont.Game.Planet.PlanetTerrainFactory.Create(planet, Horyzont.Game.Planet.PlanetTerrainOptions.Server);
        terrain.Generator = new VoxelGeneratorFlat { Channel = Horyzont.Game.Planet.TerrainChannels.Sdf, Height = 4000f };
        root.AddChild(terrain);
        var viewer = new VoxelViewer { Position = new Godot.Vector3(0, 4002, 0), ViewDistance = 64, RequiresVisuals = false, RequiresCollisions = true };
        root.AddChild(viewer);
        var space = root.GetWorld3D().DirectSpaceState;
        var from = new Godot.Vector3(0, 4010, 0);
        var to = new Godot.Vector3(0, 3990, 0);
        var hit = false;
        for (var frame = 0; frame < 3000 && !hit; frame++)
        {
            await scene.WaitFrames(1);
            hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to)).Count > 0;
        }

        AssertThat(hit).IsTrue();
        AssertThat(terrain.GetVoxelTool().IsAreaEditable(new Aabb(new Godot.Vector3(-8, 3992, -8), new Godot.Vector3(16, 16, 16)))).IsTrue();
    }
}
