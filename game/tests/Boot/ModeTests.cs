using GdUnit4;

using Godot;

using Horyzont.Core.Hashing;
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
public class HostModeTests
{
    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task Host_HasSingleTerrainInstance()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var fixture = await ModeFixture.StartAsync(
            scene, dir, true, "--host", "--bind", "127.0.0.1", "--port", ModeFixture.NextPort().ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString());
        var composition = fixture.Composition;
        AssertThat(fixture.StartExit).IsNull();
        AssertThat(await fixture.WaitInGame()).IsTrue();

        var planets = composition.Server!.State.Planets.All;
        AssertThat(CountTerrains(composition.Root)).IsEqual(planets.Count);
        AssertThat(composition.Root.GetNodeOrNull("Client/Terrain")).IsNull();
        var voxel = composition.TerrainFactory!.Terrain!.Voxel;
        foreach (var planet in planets)
        {
            AssertThat(composition.ClientTerrain!.TryGetPlanet(planet.Id, out var seenByClient)).IsTrue();
            AssertThat(voxel.TryGetPlanet(planet.Id, out var ofServer)).IsTrue();
            AssertThat(ReferenceEquals(seenByClient!.Anchor.GetParent(), ofServer)).IsTrue();
        }

        AssertThat(composition.Server.IsLocalPlayer(composition.Client!.Hello!.YourPlayer)).IsTrue();
        AssertThat(await fixture.StopAsync()).IsTrue();
        AssertThat(composition.ExitCode).IsEqual(0);
    }

    private static int CountTerrains(Node node)
    {
        var count = node is VoxelLodTerrain ? 1 : 0;
        foreach (var child in node.GetChildren())
        {
            count += CountTerrains(child);
        }

        return count;
    }
}

[TestSuite]
public class SoloModeTests
{
    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task Solo_EditSaveRestart_SameTerrainDigest()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var world = Guid.NewGuid().ToString();
        string[] args = ["--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", world];

        ulong digestBefore;
        ulong hashAfterStop;
        {
            var first = await ModeFixture.StartAsync(scene, dir, true, args);
            AssertThat(first.StartExit).IsNull();
            AssertThat(await first.WaitInGame()).IsTrue();
            var composition = first.Composition;
            var server = composition.Server!;
            var planet = server.State.Planets.All[0];
            var surface = ModeFixture.SurfaceY(planet);
            var feet = new Vector3(0, surface + 0.05f, 0);

            // The character arrives with K2-11: until then the test stands the player on the ground and loads the area around it.
            AssertThat(server.Simulation.UpdatePlayerKinematics(composition.Client!.Hello!.YourPlayer, FrameRef.Of(planet.Id), 0, feet, Quaternion.Identity, 0f)).IsTrue();
            AssertThat(composition.TerrainFactory!.Terrain!.Voxel.TryGetPlanet(planet.Id, out var node)).IsTrue();
            node!.AddChild(new VoxelViewer
            {
                Position = new Godot.Vector3(0, surface, 0),
                ViewDistance = 128,
                RequiresVisuals = false,
                RequiresCollisions = false,
            });
            var service = composition.TerrainFactory.Terrain.Voxel;
            AssertThat(await first.WaitUntil(() => service.IsAreaLoaded(planet.Id, new Vector3(0, surface, 0), 12f))).IsTrue();

            var replies = new ModeFixture.Replies();
            composition.Client.AddListener(replies);
            var eye = feet + (Vector3.UnitY * server.Simulation.Rules.EyeHeight);
            composition.Client.Session.SubmitIntent(new TerrainToolIntent(planet.Id, eye, -Vector3.UnitY, 3f, TerrainEditMode.Remove, 0));
            AssertThat(await first.WaitUntil(() => replies.All.Count == 1)).IsTrue();
            AssertThat(replies.All[0].Reason).IsEqual(RejectReason.None);
            AssertThat(server.State.TerrainVersions.All.Any()).IsTrue();

            digestBefore = ModeFixture.LiveDigest(service, server.State);
            AssertThat(await first.StopAsync()).IsTrue();
            AssertThat(composition.ExitCode).IsEqual(0);
            hashAfterStop = StateHasher.Hash(server.State);
        }

        {
            var second = await ModeFixture.StartAsync(scene, dir, false, args);
            AssertThat(second.StartExit).IsNull();
            var composition = second.Composition;
            var server = composition.Server!;
            AssertThat(StateHasher.Hash(server.State)).IsEqual(hashAfterStop);
            var planet = server.State.Planets.All[0];
            var surface = ModeFixture.SurfaceY(planet);
            var service = composition.TerrainFactory!.Terrain!.Voxel;
            AssertThat(service.TryGetPlanet(planet.Id, out var node)).IsTrue();
            node!.AddChild(new VoxelViewer
            {
                Position = new Godot.Vector3(0, surface, 0),
                ViewDistance = 128,
                RequiresVisuals = false,
                RequiresCollisions = false,
            });
            AssertThat(await second.WaitUntil(() => service.IsAreaLoaded(planet.Id, new Vector3(0, surface, 0), 12f))).IsTrue();
            AssertThat(ModeFixture.LiveDigest(service, server.State)).IsEqual(digestBefore);
            AssertThat(await second.StopAsync()).IsTrue();
        }
    }
}

[TestSuite]
public class ClientModeTests
{
    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Client_ConnectsToDedicatedInProcess_ReachesInGameWithOwnTerrain()
    {
        await using var scene = EngineTestScene.Create();
        var serverDir = ModeFixture.NewDirectory();
        var clientDir = ModeFixture.NewDirectory();
        var port = ModeFixture.NextPort();
        var config = Path.Combine(serverDir, "server.json");
        File.WriteAllText(config, ModeFixture.SimpleConfig(port));

        var server = await ModeFixture.StartAsync(scene, serverDir, false, "--server", "--config", config, "--data-dir", serverDir);
        AssertThat(server.StartExit).IsNull();
        AssertThat(server.Printed.Any(line => line.StartsWith("SERVER READY world=", StringComparison.Ordinal))).IsTrue();

        var client = await ModeFixture.StartAsync(
            scene, clientDir, true, "--connect", "127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--profile", Path.Combine(clientDir, "profile.json"));
        AssertThat(client.StartExit).IsNull();
        AssertThat(await client.WaitInGame()).IsTrue();

        var composition = client.Composition;
        var planets = composition.Client!.Hello!.Planets;
        AssertThat(planets.Count).IsEqual(server.Composition.Server!.State.Planets.All.Count);
        AssertThat(composition.Root.GetNodeOrNull("Client/Terrain")).IsNotNull();
        foreach (var planet in planets)
        {
            AssertThat(composition.ClientTerrain!.TryGetPlanet(planet.Id, out var own)).IsTrue();
            AssertThat(own!.Anchor.IsInsideTree()).IsTrue();
            AssertThat(own.Anchor.GetParent()!.GetParent()!.GetPath().ToString().EndsWith("Client/Terrain", StringComparison.Ordinal)).IsTrue();
        }

        AssertThat(server.Composition.Server.IsLocalPlayer(composition.Client.Hello!.YourPlayer)).IsFalse();
        AssertThat(await client.StopAsync()).IsTrue();
        AssertThat(composition.ExitCode).IsEqual(0);
        AssertThat(await server.StopAsync()).IsTrue();
        AssertThat(server.Composition.ExitCode).IsEqual(0);
    }
}

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
        var planet = server.State.Planets.All[0];
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
