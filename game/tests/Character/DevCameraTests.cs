using GdUnit4;

using Godot;

using Horyzont.Client;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Frames;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class DevCameraTests
{
    private static Task<ModeFixture> StartAsync(EngineTestScene scene, string dir, bool render) =>
        render
            ? ModeFixture.StartRenderedAsync(scene, dir, true, "--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString())
            : ModeFixture.StartWithCharactersAsync(scene, dir, true, true, "--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString());

    /// <summary>F9 puts the free camera in orbit; flying it down to the surface brings the visual viewer along and the terrain below is ready on arrival.</summary>
    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task DevCamera_OrbitToSurface_ViewerFollows_TerrainReadyOnArrival()
    {
        await using var scene = EngineTestScene.Create();
        var fixture = await StartAsync(scene, ModeFixture.NewDirectory(), true);
        var composition = fixture.Composition;
        AssertThat(fixture.StartExit).IsNull();
        AssertThat(await fixture.WaitInGame(120)).IsTrue();
        var player = composition.LocalPlayer!;
        AssertThat(await fixture.WaitUntil(() => player.IsActive, 180)).IsTrue();
        var dev = player.DevCamera!;
        var planet = composition.Server!.State.Planets.All[0];
        AssertThat(player.VisualViewer).IsNotNull();

        var press = new InputEventAction { Action = "dev_camera_orbit", Pressed = true };
        Input.ParseInputEvent(press);
        AssertThat(await fixture.WaitUntil(() => dev.IsActive, 30)).IsTrue();
        AssertThat(await fixture.WaitUntil(() => player.CameraMode == CameraMode.DevFree, 30)).IsTrue();
        AssertThat(dev.Position.Length()).IsEqual(DevFlyCamera.OrbitRadiusFactor * planet.RadiusMeters);

        var descending = true;
        player.InputSource = new HostLoopTests.ScriptedInput(_ => descending ? new LocalInputSample(0f, 1f, InputButtons.Sprint, 0f, 0f) : LocalInputSample.None);
        AssertThat(await fixture.WaitUntil(() => dev.Position.Length() - planet.RadiusMeters < 60f, 120)).IsTrue();
        descending = false;
        AssertThat(await fixture.WaitUntil(() => player.ClientTick > 0, 30)).IsTrue();

        // The viewer stands at the camera, the streaming settles, and the terrain under the camera is editable and meshed.
        var viewerAt = player.VisualViewer!.GlobalPosition;
        var cameraAt = PlanetFrames.ToGodot(planet.Id, dev.Position);
        AssertThat(viewerAt.DistanceTo(cameraAt)).IsLess(5f);
        AssertThat(await fixture.WaitTerrainIdle(60, 120)).IsTrue();
        AssertThat(composition.TerrainFactory!.Terrain!.Voxel.TryGetPlanet(planet.Id, out var terrain)).IsTrue();
        AssertThat(composition.TerrainFactory.Terrain.Voxel.IsAreaLoaded(planet.Id, dev.Position, 8f)).IsTrue();
        AssertThat(terrain!.Terrain.DebugGetMeshBlockCount()).IsGreater(0);
        AssertThat(player.CameraJumps).IsEqual(0);

        AssertThat(await fixture.StopAsync()).IsTrue();
    }

    /// <summary>The character flies with the dev flight: the server accepts it (dev build) and the prediction of the client agrees.</summary>
    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task DevFly_Character_ServerAndClientAgree()
    {
        await using var scene = EngineTestScene.Create();
        var fixture = await StartAsync(scene, ModeFixture.NewDirectory(), false);
        var composition = fixture.Composition;
        AssertThat(await fixture.WaitInGame(120)).IsTrue();
        var player = composition.LocalPlayer!;
        AssertThat(await fixture.WaitUntil(() => player.IsActive, 180)).IsTrue();

        player.SetDevFly(true);
        AssertThat(player.DevFlyOn).IsTrue();
        player.InputSource = new HostLoopTests.ScriptedInput(n => new LocalInputSample(((n / 90) % 2) == 0 ? 0.5f : -0.5f, 0f, (n / 45) % 2 == 0 ? InputButtons.Jump : InputButtons.Crouch, 0.002f, 0f));
        AssertThat(await fixture.WaitUntil(() => player.ServerMode == MovementMode.DevFlying, 30)).IsTrue();
        AssertThat(await fixture.WaitTerrainIdle()).IsTrue();
        player.ResetStatistics();
        var start = player.ClientTick;
        AssertThat(await fixture.WaitUntil(() => player.ClientTick >= start + 300, 60)).IsTrue();

        AssertThat(player.Compared).IsGreater(50);
        AssertThat(player.Corrections).IsEqual(0);
        // Touching the ground while flying leaves a few millimetres between the two bodies (3.7 mm seen); the correction tolerance is a centimetre.
        AssertThat(player.MaxAckPositionError).IsLess(0.001f);
        AssertThat(player.ServerMode).IsEqual(MovementMode.DevFlying);

        AssertThat(await fixture.StopAsync()).IsTrue();
    }
}
