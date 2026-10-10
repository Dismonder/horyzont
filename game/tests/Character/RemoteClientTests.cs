using System.Globalization;

using GdUnit4;

using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Tests.Support;
using Horyzont.Net.Transport.InMemory;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class RemoteClientTests
{
    /// <summary>
    /// The link of the local client gets 50 ms in each direction (RTT 100 ms) and 5 % loss: the inputs arrive late and sometimes
    /// not at all, the snapshots too. After the player stops, the prediction and the server must agree again within a centimetre.
    /// The number of corrections is reported (tuning belongs to K3-14).
    /// </summary>
    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task RemoteClient_LinkLatency100ms_ConvergesAfterCorrection()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var link = new LinkConditions(TimeSpan.FromMilliseconds(50), TimeSpan.Zero, 0.05, 0, 7);
        var fixture = await ModeFixture.StartOnLinkAsync(
            scene, dir, true, link, "--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString());
        var composition = fixture.Composition;
        AssertThat(fixture.StartExit).IsNull();
        AssertThat(await fixture.WaitInGame(120)).IsTrue();
        var player = composition.LocalPlayer!;
        AssertThat(await fixture.WaitUntil(() => player.IsActive, 180)).IsTrue();
        var playerId = composition.Client!.Hello!.YourPlayer;

        var random = new Random(3);
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
                var buttons = (random.Next(3) == 0 ? InputButtons.Sprint : InputButtons.None) | (random.Next(4) == 0 ? InputButtons.Jump : InputButtons.None);
                held = new LocalInputSample(random.Next(3) - 1, random.Next(2), buttons, 0f, 0f);
            }

            return held with { MouseYaw = (float)((random.NextDouble() - 0.5) * 0.05) };
        });
        player.ResetStatistics();
        var start = player.ClientTick;
        AssertThat(await fixture.WaitUntil(() => player.ClientTick >= start + 480, 120)).IsTrue();
        var correctionsWhileMoving = player.Corrections;

        moving = false;
        var stopped = player.ClientTick;
        AssertThat(await fixture.WaitUntil(() => player.ClientTick >= stopped + 150, 60)).IsTrue();

        AssertThat(composition.ServerCharacters!.TryGetState(playerId, out var server)).IsTrue();
        var error = System.Numerics.Vector3.Distance(server.Feet, player.PredictedFeet);
        Record($"remote_link corrections_while_moving={correctionsWhileMoving} corrections_total={player.Corrections} compared={player.Compared} final_error_mm={(error * 1000f).ToString("F3", CultureInfo.InvariantCulture)} rejected={player.RejectedSnapshots} paused={player.PredictionPaused}");
        AssertThat(error).IsLess(0.01f);
        AssertThat(player.PredictionPaused).IsFalse();

        AssertThat(await fixture.StopAsync()).IsTrue();
    }

    private static void Record(string line)
    {
        if (System.Environment.GetEnvironmentVariable("HORYZONT_RECON_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + "\n");
        }
    }
}
