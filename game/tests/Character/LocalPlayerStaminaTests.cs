using GdUnit4;

using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Character;

/// <summary>The local player removes the sprint and the jump from the input of an exhausted player before it is predicted and sent (K6-33 D20).</summary>
[TestSuite]
public class LocalPlayerStaminaTests
{
    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task Exhausted_FilteredBeforeReplayBuffer()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var fixture = await ModeFixture.StartWithCharactersAsync(
            scene, dir, true, false, "--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString());
        var composition = fixture.Composition;
        AssertThat(await fixture.WaitInGame()).IsTrue();
        var player = composition.LocalPlayer!;
        AssertThat(await fixture.WaitUntil(() => player.IsActive, 120)).IsTrue();
        AssertThat(composition.Vitals).IsNotNull();

        // The twin first: not exhausted, the same input sprints.
        player.InputSource = new HostLoopTests.ScriptedInput(_ => new LocalInputSample(0f, 1f, InputButtons.Sprint, 0f, 0f));
        var modes = new HashSet<MovementMode>();
        var start = player.ClientTick;
        AssertThat(await fixture.WaitUntil(() =>
        {
            modes.Add(player.Body!.Mode);
            return player.ClientTick >= start + 90;
        }, 60)).IsTrue();
        AssertThat(modes.Contains(MovementMode.Sprinting)).IsTrue();

        // Exhausted on both sides (the server body is not exhausted in this world, so the test says it is): the prediction stays on its feet and agrees with the server.
        composition.ServerCharacters!.ExhaustedCheck = _ => true;
        player.ExhaustedCheck = () => true;
        await fixture.WaitUntil(() => player.ClientTick >= start + 200, 60);
        modes.Clear();
        player.ResetStatistics();
        var again = player.ClientTick;
        AssertThat(await fixture.WaitUntil(() =>
        {
            modes.Add(player.Body!.Mode);
            return player.ClientTick >= again + 120;
        }, 60)).IsTrue();

        AssertThat(modes.Contains(MovementMode.Sprinting)).IsFalse();
        AssertThat(modes.Contains(MovementMode.Jumping)).IsFalse();
        AssertThat(player.Corrections).IsEqual(0);
        AssertThat(await fixture.StopAsync()).IsTrue();
    }
}
