using System.Numerics;

using GdUnit4;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class SnapshotProgressTests
{
    /// <summary>A paused airborne client follows new server ticks even when no further inputs are acknowledged.</summary>
    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task LocalPlayer_PausedWithUnchangedAck_FollowsNewSnapshots()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var fixture = await ModeFixture.StartWithCharactersAsync(
            scene, dir, true, true, "--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString());
        var composition = fixture.Composition;
        AssertThat(await fixture.WaitInGame()).IsTrue();
        var player = composition.LocalPlayer!;
        AssertThat(await fixture.WaitUntil(() => player.IsActive, 180)).IsTrue();
        var hello = composition.Client!.Hello!;
        AssertThat(await fixture.WaitUntil(() => composition.ServerCharacters!.TryGetState(hello.YourPlayer, out var s) && s.LastProcessedInputSeq > 0, 30)).IsTrue();
        AssertThat(composition.ServerCharacters!.TryGetState(hello.YourPlayer, out var server)).IsTrue();

        // Drive client ticks and incoming snapshots explicitly. Server input consumption stays stopped.
        composition.ServerNode!.SetPhysicsProcess(false);
        player.SetPhysicsProcess(false);
        player.SetDevFly(true);
        player.InputSource = new HostLoopTests.ScriptedInput(_ => LocalInputSample.None);
        var feet = server.Feet + (Vector3.Normalize(server.Feet) * 50f);
        Snapshot SnapshotAt(uint id, Vector3 position) => new(
            id, new Tick(id), 0, server.LastProcessedInputSeq,
            [new SnapshotFrame(server.Frame, server.ParentSeq)],
            [new EntityState(hello.YourEntity, (EntityStateFields)15, 0, server.ParentSeq, position, server.BodyRotation, Vector3.UnitX, (byte)MovementMode.DevFlying)]);

        player.OnFullSnapshot(SnapshotAt(1_000_000, feet));
        for (var i = 0; i < 130; i++)
        {
            player._PhysicsProcess(1d / hello.TickRate);
        }

        AssertThat(player.PredictionPaused).IsTrue();
        for (uint id = 1_000_001; id <= 1_000_003; id++)
        {
            feet += Vector3.UnitX;
            player.OnFullSnapshot(SnapshotAt(id, feet));
            player._PhysicsProcess(1d / hello.TickRate);
            AssertThat(Vector3.Distance(player.PredictedFeet, feet)).IsLess(0.001f);
            AssertThat(player.PredictionPaused).IsTrue();
        }

        composition.ServerNode.SetPhysicsProcess(true);
        player.SetPhysicsProcess(true);
        AssertThat(await fixture.StopAsync()).IsTrue();
    }
}
