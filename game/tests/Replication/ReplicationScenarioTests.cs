using System.Globalization;

using GdUnit4;

using Horyzont.Client.Tools;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Client;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Tools;

using static GdUnit4.Assertions;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Replication;

[TestSuite]
public class ReplicationScenarioTests
{
    /// <summary>Solo and host share one world with their server: one construct world, the shared terrain, no mirror.</summary>
    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task Replicate_HostLocal_UsesOneConstructWorldAndTerrain()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var fixture = await ModeFixture.StartWithCharactersAsync(
            scene, dir, true, false, "--host", "--bind", "127.0.0.1", "--port", ModeFixture.NextPort().ToString(CultureInfo.InvariantCulture),
            "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString());
        var composition = fixture.Composition;
        AssertThat(fixture.StartExit).IsNull();
        AssertThat(await fixture.WaitInGame(120)).IsTrue();

        AssertThat(composition.Constructs).IsNotNull();
        AssertThat(composition.RemoteConstructs).IsNull();
        AssertThat(composition.RemoteTools).IsNull();
        AssertThat(composition.ClientReplication).IsNotNull();
        AssertThat(composition.ClientReplication!.Constructs).IsNull();
        AssertThat(composition.ClientTerrain is SharedTerrainProvider).IsTrue();
        AssertThat(composition.Root.FindChildren("Constructs", string.Empty, true, false).Count).IsEqual(1);
        AssertThat(await fixture.WaitUntil(() => composition.Replication?.TrackedPlayers == 1, 30)).IsTrue();
        AssertThat(composition.Replication!.Constructs.StagedBytes(composition.Client!.Hello!.YourPlayer)).IsEqual(0);

        AssertThat(await fixture.StopAsync()).IsTrue();
    }

    /// <summary>
    /// A dedicated server and a remote client over loopback UDP: a construct built next to the player appears in the mirror and in the
    /// construct world, and the real tool node places and removes blocks, which the server validates and the overlay waits for.
    /// </summary>
    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task RemoteTools_BuildAndClear_UsesMirrorAndServerValidation()
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
        AssertThat(await client.WaitTerrainIdle()).IsTrue();
        AssertThat(composition.RemoteTools).IsNotNull();
        AssertThat(composition.RemoteConstructs).IsNotNull();
        var tools = composition.RemoteTools!;
        AssertThat(tools.Inventory).IsNotNull(); // the hotbar selects the tool and an open inventory blocks it on a remote client too
        var replies = new List<Horyzont.Core.Protocol.Messages.IntentReply>();
        tools.IntentAnswered += (reply, _) => replies.Add(reply);
        var script = new ToolRig.ScriptedTools();
        tools.InputSource = script;

        // The server builds a one-block construct that the aim ray of the player passes through, 3 m ahead of the eye.
        var runtime = server.Composition.Server!;
        runtime.State.BlockTypes.TryGetId("core:stone_block", out var stone);
        // Measured from the eye, not the third person camera: a construct between the camera and the character is hit by the camera arm
        // as soon as it replicates, which moves the camera in front of the construct, facing away from it.
        var eye = Horyzont.Client.Tools.AimRayBuilder.EyeFromFeet(player.PredictedFeet, new Horyzont.Core.Simulation.GameRules());
        var target = eye + (player.CameraDirection * 3f);
        var id = runtime.State.EntityIds.PeekNext();
        var frame = Horyzont.Core.Ids.FrameRef.Of(player.Planet);
        runtime.Simulation.ApplyServerOperation(new ConstructCreateOp(
            id, frame, target - new NumVector3(0.5f, 0.5f, 0.5f), System.Numerics.Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(stone, 0)));
        var mirror = composition.ClientReplication!.Constructs!;
        AssertThat(await client.WaitUntil(() => mirror.TryGetConstruct(id, out _), 30)).IsTrue();
        AssertThat(await client.WaitUntil(() => composition.RemoteConstructs!.World!.TryGetConstruct(id, out _), 30)).IsTrue();

        // Place a block on the construct with the builder tool.
        script.Press(ToolRig.Frame(select: ToolKind.Builder));
        AssertThat(await client.WaitUntil(() => tools.Preview.Kind == ToolPreviewKind.ConstructCell, 30)).IsTrue();
        script.Press(ToolRig.Frame(secondaryPressed: true));
        AssertThat(await client.WaitUntil(() => replies.Count >= 1, 30)).IsTrue();
        AssertThat(replies[0].Reason).IsEqual(Horyzont.Core.Intents.RejectReason.None);
        AssertThat(await client.WaitUntil(() => mirror.TryGetConstruct(id, out var built) && built.BlockCount == 2, 30)).IsTrue();
        AssertThat(await client.WaitUntil(() => composition.BlockPrediction!.Count == 0, 30)).IsTrue();
        AssertThat(runtime.State.Constructs.TryGet(id, out var authoritative) && authoritative!.Version == 2).IsTrue();

        // Remove it again: the server validates, the copy and the world follow.
        script.Press(ToolRig.Frame(primaryPressed: true));
        AssertThat(await client.WaitUntil(() => replies.Count >= 2, 30)).IsTrue();
        AssertThat(await client.WaitUntil(() => mirror.TryGetConstruct(id, out var cleared) && cleared.Version == authoritative!.Version, 30)).IsTrue();
        AssertThat(await client.WaitUntil(() => composition.BlockPrediction!.Count == 0, 30)).IsTrue();

        AssertThat(await client.StopAsync()).IsTrue();
        AssertThat(await server.StopAsync()).IsTrue();
    }
}
