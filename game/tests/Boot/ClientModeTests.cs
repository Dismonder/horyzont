using GdUnit4;

using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Boot;

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

        // K5-22 decision 10: a client creates the terrain of a planet only on activation, which the own body (the characters) drives,
        // so both sides compose with the characters.
        var server = await ModeFixture.StartWithCharactersAsync(scene, serverDir, false, false, "--server", "--config", config, "--data-dir", serverDir);
        AssertThat(server.StartExit).IsNull();
        AssertThat(server.Printed.Any(line => line.StartsWith("SERVER READY world=", StringComparison.Ordinal))).IsTrue();

        var client = await ModeFixture.StartWithCharactersAsync(
            scene, clientDir, true, false, "--connect", "127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--profile", Path.Combine(clientDir, "profile.json"));
        AssertThat(client.StartExit).IsNull();
        AssertThat(await client.WaitInGame()).IsTrue();

        var composition = client.Composition;
        var planets = composition.Client!.Hello!.Planets;
        AssertThat(planets.Count).IsEqual(server.Composition.Server!.State.Planets.All.Count);
        AssertThat(composition.Root.GetNodeOrNull("Client/Terrain")).IsNotNull();
        // K5-21 decision 10: element 0 of the hello is the home planet; K5-22: at start it is the only one with an active terrain (the other bodies are farther than 4 R).
        AssertThat(planets[0].Id).IsEqual(server.Composition.Server!.State.HomePlanet);
        // K5-22 decision 10: the client creates the terrain of the home planet when its own body is created (the first own state),
        // which may come a few frames after InGame.
        AssertThat(await client.WaitUntil(() => composition.ClientTerrain!.TryGetPlanet(planets[0].Id, out _), 120)).IsTrue();
        AssertThat(composition.ClientTerrain!.TryGetPlanet(planets[0].Id, out var own)).IsTrue();
        AssertThat(own!.Anchor.IsInsideTree()).IsTrue();
        // K5-22: the client terrain node hangs under the client frame node of its planet (Client/Frames/System_n/Planet_<id>), not the server's.
        var planetNode = own.Anchor.GetParent()!.GetParent()!.GetPath().ToString();
        AssertThat(planetNode).Contains("/Client/Frames/");
        AssertThat(planetNode).EndsWith("/Planet_" + planets[0].Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var body in planets.Skip(1))
        {
            AssertThat(composition.ClientTerrain.TryGetPlanet(body.Id, out _)).IsFalse();
        }

        AssertThat(server.Composition.Server.IsLocalPlayer(composition.Client.Hello!.YourPlayer)).IsFalse();
        AssertThat(await client.StopAsync()).IsTrue();
        AssertThat(composition.ExitCode).IsEqual(0);
        AssertThat(await server.StopAsync()).IsTrue();
        AssertThat(server.Composition.ExitCode).IsEqual(0);
    }
}
