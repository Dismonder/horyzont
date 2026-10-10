using System.Numerics;

using GdUnit4;

using Horyzont.Client;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class CoopSeamTests
{
    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task LocalPlayer_WorldNotReady_FollowsServerWithoutSendingInputs()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        // Rendered parts: the visual viewer exists only with rendering (a solo host shares the server's collisions, so it has no collision viewer).
        var fixture = await ModeFixture.StartRenderedAsync(scene, dir, true,
            "--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString());
        var composition = fixture.Composition;
        AssertThat(await fixture.WaitInGame()).IsTrue();
        var player = composition.LocalPlayer!;
        AssertThat(await fixture.WaitUntil(() => player.IsActive, 180)).IsTrue();
        var hello = composition.Client!.Hello!;
        AssertThat(composition.ServerCharacters!.TryGetState(hello.YourPlayer, out var server)).IsTrue();
        composition.ServerNode!.SetPhysicsProcess(false);
        composition.ClientNode!.SetPhysicsProcess(false);
        player.SetPhysicsProcess(false);
        player.WorldReadiness = new ClosedWorld();
        // Drain inputs already sent during settling before closing readiness.
        for (var i = 0; i < 20; i++)
        {
            composition.Server!.Tick();
        }

        AssertThat(composition.ServerCharacters.TryGetState(hello.YourPlayer, out server)).IsTrue();
        var sent = server.LastProcessedInputSeq;
        var feet = server.Feet + Vector3.Normalize(server.Feet) * 50f;
        for (uint id = 1000000; id < 1000010; id++)
        {
            feet += Vector3.UnitX;
            player.OnFullSnapshot(new Snapshot(id, new Tick(id), 0, server.LastProcessedInputSeq,
                [new SnapshotFrame(server.Frame, server.ParentSeq)],
                [new EntityState(hello.YourEntity, (EntityStateFields)15, 0, server.ParentSeq, feet, server.BodyRotation, Vector3.Zero, (byte)MovementMode.DevFlying)]));
            player._PhysicsProcess(1d / hello.TickRate);
            AssertThat(Vector3.Distance(player.PredictedFeet, feet)).IsLess(0.001f);
        }

        for (var i = 0; i < 20; i++)
        {
            composition.Server!.Tick();
        }

        AssertThat(composition.ServerCharacters.TryGetState(hello.YourPlayer, out var after)).IsTrue();
        AssertThat(after.LastProcessedInputSeq).IsEqual(sent);
        AssertThat(player.TryGetViewers(out var viewers)).IsTrue();
        AssertThat(viewers.Planet).IsEqual(player.Planet);
        AssertThat(viewers.CollisionDistance).IsEqual(0u);
        AssertThat(viewers.VisualDistance).IsGreater(0u);
        composition.ServerNode.SetPhysicsProcess(true);
        composition.ClientNode.SetPhysicsProcess(true);
        player.SetPhysicsProcess(true);
        AssertThat(await fixture.StopAsync()).IsTrue();
    }

    private sealed class ClosedWorld : IClientWorldReadiness
    {
        public bool IsReady => false;
    }
}
