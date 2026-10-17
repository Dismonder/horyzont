using GdUnit4;

using Godot;

using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

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

        // K5-22: terrains are activated on demand; at start only the home planet (the region of the player) has terrain, the other bodies of the star system are farther than 4 R and have none.
        var state = composition.Server!.State;
        var home = ModeFixture.Home(state);
        AssertThat(state.Planets.All.Count).IsGreater(1);
        AssertThat(CountTerrains(composition.Root)).IsEqual(1);
        AssertThat(composition.Root.GetNodeOrNull("Client/Terrain")).IsNull();
        var voxel = composition.TerrainFactory!.Terrain!.Voxel;
        AssertThat(composition.ClientTerrain!.TryGetPlanet(home.Id, out var seenByClient)).IsTrue();
        AssertThat(voxel.TryGetPlanet(home.Id, out var ofServer)).IsTrue();
        AssertThat(ReferenceEquals(seenByClient!.Anchor.GetParent(), ofServer)).IsTrue();
        foreach (var body in state.Planets.All.Where(planet => planet.Id != home.Id))
        {
            AssertThat(composition.ClientTerrain.TryGetPlanet(body.Id, out _)).IsFalse();
            AssertThat(voxel.TryGetPlanet(body.Id, out _)).IsFalse();
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
