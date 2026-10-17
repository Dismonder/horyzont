using GdUnit4;

using Godot;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

using Quaternion = System.Numerics.Quaternion;
using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Boot;

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
            var planet = ModeFixture.Home(server.State);
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
            var planet = ModeFixture.Home(server.State);
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
