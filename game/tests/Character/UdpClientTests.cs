using System.Globalization;

using GdUnit4;

using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class UdpClientTests
{
    /// <summary>A dedicated server and a client of the same process talk over real UDP on the loopback: the client walks, the server agrees.</summary>
    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task RemoteClient_OverLoopbackUdp_WalksAndAgreesWithTheServer()
    {
        await using var scene = EngineTestScene.Create();
        var serverDir = ModeFixture.NewDirectory();
        var clientDir = ModeFixture.NewDirectory();
        var port = ModeFixture.NextPort();
        var config = Path.Combine(serverDir, "server.json");
        File.WriteAllText(config, ModeFixture.SimpleConfig(port));
        var server = await ModeFixture.StartWithCharactersAsync(scene, serverDir, false, false, "--server", "--config", config, "--data-dir", serverDir);
        AssertThat(server.StartExit).IsNull();

        var client = await ModeFixture.StartWithCharactersAsync(
            scene, clientDir, true, false, "--connect", "127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture), "--profile", Path.Combine(clientDir, "profile.json"));
        AssertThat(client.StartExit).IsNull();
        AssertThat(await client.WaitInGame(120)).IsTrue();
        var composition = client.Composition;
        var player = composition.LocalPlayer!;
        AssertThat(await client.WaitUntil(() => player.IsActive, 180)).IsTrue();
        var playerId = composition.Client!.Hello!.YourPlayer;

        var random = new Random(21);
        var held = new LocalInputSample(0f, 1f, InputButtons.None, 0f, 0f);
        var moving = true;
        player.InputSource = new HostLoopTests.ScriptedInput(n =>
        {
            if (!moving)
            {
                return LocalInputSample.None;
            }

            if (n % 50 == 1)
            {
                held = new LocalInputSample(random.Next(3) - 1, random.Next(2), random.Next(4) == 0 ? InputButtons.Jump : InputButtons.None, 0f, 0f);
            }

            return held with { MouseYaw = (float)((random.NextDouble() - 0.5) * 0.05) };
        });
        AssertThat(await client.WaitTerrainIdle()).IsTrue();
        player.ResetStatistics();
        var start = player.ClientTick;
        AssertThat(await client.WaitUntil(() => player.ClientTick >= start + 480, 120)).IsTrue();
        var whileMoving = player.Corrections;

        moving = false;
        var stopped = player.ClientTick;
        AssertThat(await client.WaitUntil(() => player.ClientTick >= stopped + 120, 60)).IsTrue();

        AssertThat(server.Composition.ServerCharacters!.TryGetState(playerId, out var state)).IsTrue();
        var error = System.Numerics.Vector3.Distance(state.Feet, player.PredictedFeet);
        Record($"udp_loopback corrections_while_moving={whileMoving} total={player.Corrections} compared={player.Compared} final_error_mm={(error * 1000f).ToString("F3", CultureInfo.InvariantCulture)} rejected={player.RejectedSnapshots}");
        AssertThat(error).IsLess(0.01f);
        AssertThat(player.Compared).IsGreater(100);
        AssertThat(player.PredictionPaused).IsFalse();
        AssertThat(player.CameraJumps).IsEqual(0);

        AssertThat(await client.StopAsync()).IsTrue();
        AssertThat(await server.StopAsync()).IsTrue();
    }

    /// <summary>
    /// A debug client started with --dev against a server without it predicts flight while the server walks: after a few corrections in a
    /// row the client switches the flight off and tells the player.
    /// </summary>
    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task RemoteClient_DevFlyRefusedByServer_TurnsFlightOffAndShowsNotice()
    {
        await using var scene = EngineTestScene.Create();
        var serverDir = ModeFixture.NewDirectory();
        var clientDir = ModeFixture.NewDirectory();
        var port = ModeFixture.NextPort();
        var config = Path.Combine(serverDir, "server.json");
        File.WriteAllText(config, ModeFixture.SimpleConfig(port));
        var server = await ModeFixture.StartWithCharactersAsync(scene, serverDir, false, false, "--server", "--config", config, "--data-dir", serverDir);
        AssertThat(server.StartExit).IsNull();
        var client = await ModeFixture.StartWithCharactersAsync(
            scene, clientDir, true, false, "--connect", "127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture), "--dev", "--profile", Path.Combine(clientDir, "profile.json"));
        AssertThat(client.StartExit).IsNull();
        AssertThat(await client.WaitInGame(120)).IsTrue();
        var player = client.Composition.LocalPlayer!;
        AssertThat(await client.WaitUntil(() => player.IsActive, 180)).IsTrue();
        AssertThat(await client.WaitTerrainIdle()).IsTrue();

        player.InputSource = new HostLoopTests.ScriptedInput(_ => new LocalInputSample(0f, 0f, InputButtons.Jump, 0f, 0f));
        player.SetDevFly(true);
        AssertThat(player.DevFlyOn).IsTrue();
        AssertThat(await client.WaitUntil(() => !player.DevFlyOn, 60)).IsTrue();

        AssertThat(player.Notice).IsEqual("serwer nie pozwala na lot");
        AssertThat(player.Corrections).IsGreaterEqual(LocalPlayer.DevFlyRefusalCorrections);
        AssertThat(player.Corrections).IsLess(3 * LocalPlayer.DevFlyRefusalCorrections);

        AssertThat(await client.StopAsync()).IsTrue();
        AssertThat(await server.StopAsync()).IsTrue();
    }

    private static void Record(string line)
    {
        if (System.Environment.GetEnvironmentVariable("HORYZONT_RECON_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + "\n");
        }
    }
}
