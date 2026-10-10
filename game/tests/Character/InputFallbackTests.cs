using GdUnit4;

using Horyzont.Core.Motion;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class InputFallbackTests
{
    /// <summary>An invalid input becomes the same neutral input on the client and the server: facing the last valid yaw and pitch.</summary>
    [TestCase]
    [RequireGodotRuntime]
    public void LocalPlayer_InvalidInput_FallsBackToNeutralWithTheLastValidView()
    {
        var movement = new MovementRules();
        var yaw = 0f;
        var pitch = 0f;
        var good = LocalPlayer.ValidMotor(new PlayerInput(1, 0f, 1f, 1.25f, 0.4f, InputButtons.None), movement, ref yaw, ref pitch);
        AssertThat(good.Yaw).IsEqual(yaw);
        AssertThat(yaw).IsEqual(1.25f);

        var bad = LocalPlayer.ValidMotor(new PlayerInput(2, float.NaN, 1f, 3f, 0.1f, InputButtons.Jump), movement, ref yaw, ref pitch);

        var expected = MotorInput.Neutral(1.25f, pitch);
        AssertThat(bad).IsEqual(expected);
        AssertThat(yaw).IsEqual(1.25f);
    }

    /// <summary>The replication state (snapshot history, interest) of a player is dropped when the player leaves.</summary>
    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task EntityReplication_PlayerLeaves_StateForgotten()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var fixture = await ModeFixture.StartWithCharactersAsync(
            scene, dir, true, false, "--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString());
        var composition = fixture.Composition;
        AssertThat(await fixture.WaitInGame(120)).IsTrue();
        AssertThat(await fixture.WaitUntil(() => composition.Replication?.TrackedPlayers == 1, 30)).IsTrue();
        var player = composition.Client!.Hello!.YourPlayer;

        composition.Replication!.OnPlayerLeaving(player, DisconnectCode.Normal);
        AssertThat(composition.Replication.TrackedPlayers).IsEqual(0);
        AssertThat(await fixture.WaitUntil(() => composition.Replication.TrackedPlayers == 1, 30)).IsTrue();

        var replication = composition.Replication;
        AssertThat(await fixture.StopAsync()).IsTrue();
        AssertThat(replication.TrackedPlayers).IsEqual(0);
    }
}
