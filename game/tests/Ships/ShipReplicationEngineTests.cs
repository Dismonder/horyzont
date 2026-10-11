using GdUnit4;

using Horyzont.Game.Tests.Support;
using Horyzont.Server.Ships;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Ships;

/// <summary>K5-26 composition: the server sends <c>ShipStatus</c> (31) and the client keeps it for the flight HUD.</summary>
[TestSuite]
public class ShipReplicationEngineTests
{
    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task Host_ComposesShipStatusSenderAndClientShipStatus()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var fixture = await ModeFixture.StartAsync(
            scene, dir, true, "--host", "--bind", "127.0.0.1", "--port", ModeFixture.NextPort().ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString());
        var composition = fixture.Composition;
        AssertThat(fixture.StartExit).IsNull();
        AssertThat(await fixture.WaitInGame()).IsTrue();

        AssertThat(composition.Server!.TryGetTickParticipant<ShipStatusSender>(out _)).IsTrue();
        AssertThat(composition.ClientShipStatus).IsNotNull();

        // The player stands on the planet: no ship status reaches the client.
        AssertThat(await fixture.WaitUntil(() => composition.Server.State.CurrentTick.Value > 3 * ShipStatusSender.IntervalTicks, 30)).IsTrue();
        AssertThat(composition.ClientShipStatus!.TryGet(new Horyzont.Core.Ids.EntityId(1), out _)).IsFalse();

        AssertThat(await fixture.StopAsync()).IsTrue();
        AssertThat(composition.ExitCode).IsEqual(0);
    }
}
