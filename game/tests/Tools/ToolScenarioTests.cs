using System.Diagnostics;
using System.Globalization;

using GdUnit4;

using Godot;

using Horyzont.Client.Tools;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Game.Client.Tools;
using Horyzont.Game.Constructs;
using Horyzont.Game.Planet;
using Horyzont.Game.Tests.Support;

using Microsoft.Extensions.Logging.Abstractions;

using static GdUnit4.Assertions;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Tools;

/// <summary>K2-13 scenarios of the tools on the real terrain and the real construct world (the K2 milestone: dig, fill, place, remove, save).</summary>
[TestSuite]
public class ToolScenarioTests
{
    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task Solo_RestartWithPlayerOnConstruct_KeepsSavedRoofPose()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var world = Guid.NewGuid();
        var first = await ToolRig.StartSoloAsync(scene, dir, world, lookDown: false);
        var server = first.Composition.Server!;
        server.State.Players.TryGet(first.PlayerId, out var player);
        var up = Horyzont.Core.World.Terrain.TerrainMath.UpAt(player!.Position);
        var rotation = player.BodyRotation;
        var origin = player.Position + (up * 4) - NumVector3.Transform(new NumVector3(0.5f, 0, 0.5f), rotation);
        var saved = player.Position + (up * 5.01f);
        server.State.BlockTypes.TryGetId("core:stone_block", out var type);
        var id = server.State.EntityIds.PeekNext();
        server.Simulation.ApplyServerOperation(new ConstructCreateOp(id, player.Frame, origin, rotation,
            new Horyzont.Core.World.Constructs.CellPos(0, 0, 0), Horyzont.Core.World.Constructs.BlockState.Create(type, 0)));
        AssertThat(await first.Fixture.WaitUntil(() => first.Composition.Constructs!.AreSpawnCollisionsReady(first.Player.Planet, saved), 120)).IsTrue();
        first.Composition.ServerCharacters!.GetNode<Horyzont.Game.Character.PlayerBody>($"Body_{first.PlayerId.Value}").Place(
            saved, NumVector3.Zero, rotation, Horyzont.Core.Motion.MovementMode.Walking, false);
        AssertThat(await first.Fixture.StopAsync()).IsTrue();
        var second = await ToolRig.StartSoloAsync(scene, dir, world, lookDown: false);
        AssertThat(second.Composition.ServerCharacters!.TryGetState(second.PlayerId, out var restored)).IsTrue();
        AssertThat(NumVector3.Distance(restored.Feet, saved)).IsLess(0.2f);
        AssertThat(await second.Fixture.StopAsync()).IsTrue();
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task SoloDigAndFill_RealTerrain()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await ToolRig.StartSoloAsync(scene, ModeFixture.NewDirectory(), Guid.NewGuid());
        var service = rig.Composition.TerrainFactory!.Terrain!.Voxel;
        var planet = rig.Player.Planet;
        AssertThat(service.TryGetPlanet(planet, out var terrain)).IsTrue();
        var digAt = rig.Tools.Preview.Center;
        var probeDown = -Horyzont.Core.World.Terrain.TerrainMath.UpAt(digAt);
        var probeFrom = digAt - (probeDown * 3f);
        AssertThat(service.Raycast(planet, probeFrom, probeDown, 30f, out var surface)).IsTrue();

        rig.Script.Press(ToolRig.Frame(primaryPressed: true, radiusSteps: 4));
        AssertThat(await rig.WaitReplies(1)).IsTrue();
        AssertThat(rig.Replies[0].Reason).IsEqual(RejectReason.None);
        AssertThat(rig.Tools.Settings!.Value.Radius).IsEqual(2f);
        AssertThat(service.Raycast(planet, probeFrom, probeDown, 30f, out var crater)).IsTrue();
        var tool = terrain!.Tool;
        tool.Channel = TerrainChannels.Sdf;
        var sdfAtCenter = tool.GetVoxelF(new Vector3I((int)MathF.Round(digAt.X), (int)MathF.Round(digAt.Y), (int)MathF.Round(digAt.Z)));
        Record($"dig surface={surface.Distance:F2} crater={crater.Distance:F2} sdf_center={sdfAtCenter:F2}");
        AssertThat(crater.Distance).IsGreater(surface.Distance + 1.5f);
        AssertThat(sdfAtCenter).IsGreater(0.5f);

        // Fill the floor of the crater with the second material (dirt) with a smaller sphere.
        AssertThat(await rig.WaitTicks(3)).IsTrue();
        var fillAt = rig.Tools.Preview.Center;
        AssertThat(rig.Tools.Preview.Kind).IsEqual(ToolPreviewKind.Sphere);
        rig.Script.Press(ToolRig.Frame(secondaryPressed: true, materialSteps: 1, radiusSteps: -4));
        AssertThat(await rig.WaitReplies(2)).IsTrue();
        AssertThat(rig.Replies[1].Reason).IsEqual(RejectReason.None);
        var dirt = rig.Tools.Settings!.Value.FillMaterial;
        AssertThat(rig.Tools.Materials!.Entries.First(e => e.Id == dirt).Key).IsEqual(TerrainMaterials.DirtKey);
        var voxel = new Vector3I((int)MathF.Round(fillAt.X), (int)MathF.Round(fillAt.Y), (int)MathF.Round(fillAt.Z));
        var sdfFilled = tool.GetVoxelF(voxel);
        tool.Channel = TerrainChannels.Material;
        var texture = tool.GetVoxel(voxel);
        tool.Channel = TerrainChannels.Sdf;
        Record($"fill sdf={sdfFilled:F2} texture={texture} dirt_texture={TerrainMaterials.DirtTextureIndex}");
        AssertThat(sdfFilled).IsLess(0f);
        AssertThat(texture).IsEqual((ulong)TerrainMaterials.DirtTextureIndex);
        AssertThat(rig.Composition.Server!.State.TerrainVersions.Count).IsGreater(0);
        AssertThat(await rig.Fixture.StopAsync()).IsTrue();
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task SoloPlaceAndRemoveBlock_MeshUpdates()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await ToolRig.StartSoloAsync(scene, ModeFixture.NewDirectory(), Guid.NewGuid());
        var world = rig.Composition.Constructs!.World;
        var state = rig.Composition.Server!.State;
        var replyAt = 0L;
        var meshAt = 0L;
        rig.Tools.IntentAnswered += (_, _) => replyAt = Stopwatch.GetTimestamp();
        world.ChunkMeshApplied += (_, _) => meshAt = Stopwatch.GetTimestamp();

        rig.Script.Press(ToolRig.Frame(select: ToolKind.Builder));
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Preview.Kind == ToolPreviewKind.NewConstructCell, 20)).IsTrue();
        AssertThat(rig.Tools.Preview.Hint).IsEqual(RejectReason.None);
        rig.Script.Press(ToolRig.Frame(secondaryPressed: true));
        AssertThat(await rig.WaitReplies(1)).IsTrue();
        AssertThat(rig.Replies[0].Reason).IsEqual(RejectReason.None);
        AssertThat(state.Constructs.Count).IsEqual(1);
        var construct = state.Constructs.All[0];
        AssertThat(await rig.Fixture.WaitUntil(() => meshAt != 0 && world.TryGetConstructNode(construct.Id, out _), 10)).IsTrue();
        var placedMs = Stopwatch.GetElapsedTime(replyAt, meshAt).TotalMilliseconds;
        Record($"place mesh_after_reply_ms={placedMs.ToString("F1", CultureInfo.InvariantCulture)}");
        AssertThat(placedMs).IsLess(100d);

        // The block is now the first thing the eye ray meets: the builder removes it with the primary action.
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Preview.Kind == ToolPreviewKind.ConstructCell, 20)).IsTrue();
        rig.Script.Press(ToolRig.Frame(primaryPressed: true));
        AssertThat(await rig.WaitReplies(2)).IsTrue();
        AssertThat(rig.Replies[1].Reason).IsEqual(RejectReason.None);
        var removedAt = Stopwatch.GetTimestamp();
        AssertThat(await rig.Fixture.WaitUntil(() => !world.TryGetConstructNode(construct.Id, out _), 5)).IsTrue();
        AssertThat(Stopwatch.GetElapsedTime(replyAt, Stopwatch.GetTimestamp()).TotalMilliseconds).IsLess(150d);
        AssertThat(Stopwatch.GetElapsedTime(removedAt, Stopwatch.GetTimestamp()).TotalMilliseconds).IsLess(100d);
        AssertThat(state.Constructs.Count).IsEqual(0);
        AssertThat(rig.Tools.Pending!.Count).IsEqual(0);
        AssertThat(await rig.Fixture.StopAsync()).IsTrue();
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task HostConstructs_WiredInComposition()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await ToolRig.StartHostAsync(scene, ModeFixture.NewDirectory(), Guid.NewGuid());
        var composition = rig.Composition;
        var constructs = composition.Constructs!;
        AssertThat(constructs.IsAttached).IsTrue();
        AssertThat(CountOf<ConstructWorld>(composition.Root)).IsEqual(1);
        AssertThat(composition.Server!.State.BlockTypes.Entries.Count).IsGreater(1);

        rig.Script.Press(ToolRig.Frame(select: ToolKind.Builder));
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Preview.Kind == ToolPreviewKind.NewConstructCell, 20)).IsTrue();
        rig.Script.Press(ToolRig.Frame(secondaryPressed: true));
        AssertThat(await rig.WaitReplies(1)).IsTrue();
        AssertThat(rig.Replies[0].Reason).IsEqual(RejectReason.None);
        var id = composition.Server.State.Constructs.All[0].Id;
        AssertThat(await rig.Fixture.WaitUntil(() => constructs.World.TryGetConstructNode(id, out _), 10)).IsTrue();

        AssertThat(await rig.Fixture.StopAsync()).IsTrue();
        var steps = composition.Server.CompletedShutdownSteps;
        var terrain = steps.ToList().IndexOf("7 shut terrain down");
        var game = steps.ToList().IndexOf("7a shut game systems down");
        var transport = steps.ToList().IndexOf("8 dispose transport");
        Record("host_steps " + string.Join(" | ", steps));
        AssertThat(terrain >= 0 && game == terrain + 1 && transport == game + 1).IsTrue();
        AssertThat(constructs.World.IsShutdown).IsTrue();
        AssertThat(constructs.World.AliveWorkerThreads).IsEqual(0);
        AssertThat(composition.ExitCode).IsEqual(0);
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task HostConstructs_FailedViewUpdate_ResyncsOnceOnTheNextTick()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await ToolRig.StartSoloAsync(scene, ModeFixture.NewDirectory(), Guid.NewGuid());
        var constructs = rig.Composition.Constructs!;
        var world = constructs.World;
        var failures = 0;
        constructs.ViewUpdate = (_, _) =>
        {
            failures++;
            throw new InvalidOperationException("injected");
        };

        rig.Script.Press(ToolRig.Frame(select: ToolKind.Builder));
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Preview.Kind == ToolPreviewKind.NewConstructCell, 20)).IsTrue();
        rig.Script.Press(ToolRig.Frame(secondaryPressed: true));
        AssertThat(await rig.WaitReplies(1)).IsTrue();
        AssertThat(rig.Replies[0].Reason).IsEqual(RejectReason.None);
        AssertThat(failures).IsEqual(1);

        // The view missed the create; the resync of the next tick rebuilds it from the state, and nothing keeps rebuilding afterwards.
        var id = rig.Composition.Server!.State.Constructs.All[0].Id;
        AssertThat(await rig.Fixture.WaitUntil(() => world.TryGetConstructNode(id, out _), 10)).IsTrue();
        AssertThat(constructs.ResyncCount).IsEqual(1);
        AssertThat(await rig.WaitTicks(40)).IsTrue();
        AssertThat(constructs.ResyncCount).IsEqual(1);
        AssertThat(await rig.Fixture.StopAsync()).IsTrue();
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task DevToolsDemo_OnlyInADevelopmentBuild()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        string[] args = ["--solo", "--dev-tools-demo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString()];

        string? rejected = null;
        try
        {
            await ModeFixture.StartWithCharactersAsync(scene, dir, true, false, args);
        }
        catch (ArgumentException ex)
        {
            rejected = ex.Message;
        }

        AssertThat(rejected is not null && rejected.Contains("--dev-tools-demo", StringComparison.Ordinal)).IsTrue();

        var fixture = await ModeFixture.StartWithCharactersAsync(scene, dir, true, true, args);
        AssertThat(fixture.Composition.Tools is not null).IsTrue();
        AssertThat(await fixture.StopAsync()).IsTrue();
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task Solo_NothingInReach_SendsNothing()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await ToolRig.StartSoloAsync(scene, ModeFixture.NewDirectory(), Guid.NewGuid());
        var pitched = false;
        rig.Player.InputSource = new ToolRig.Scripted(_ =>
        {
            if (pitched)
            {
                return default;
            }

            pitched = true;
            return new Horyzont.Game.Client.Character.LocalInputSample(0f, 0f, Horyzont.Core.Protocol.Messages.InputButtons.None, 0f, 1.4f);
        });
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Preview.Kind == ToolPreviewKind.None, 30)).IsTrue();
        rig.Script.Repeat = () => ToolRig.Frame(primaryHeld: true);
        AssertThat(await rig.WaitTicks(40)).IsTrue();
        rig.Script.Repeat = null;
        AssertThat(rig.Replies.Count).IsEqual(0);
        AssertThat(rig.Tools.Pending!.Count).IsEqual(0);
        AssertThat(rig.Composition.Server!.State.TerrainVersions.Count).IsEqual(0);
        AssertThat(await rig.Fixture.StopAsync()).IsTrue();
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task Solo_HoldDig_AtMostFiveEditsInAnyOneSecondWindow()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await ToolRig.StartSoloAsync(scene, ModeFixture.NewDirectory(), Guid.NewGuid());
        var ticks = new List<ulong>();
        rig.Composition.Server!.Simulation.Applied += applied =>
        {
            if (applied.Operation is TerrainEditOp)
            {
                ticks.Add(applied.Tick.Value);
            }
        };

        rig.Script.Press(ToolRig.Frame(radiusSteps: -2));
        AssertThat(await rig.WaitTicks(2)).IsTrue();
        rig.Script.Repeat = () => ToolRig.Frame(primaryHeld: true);
        AssertThat(await rig.WaitTicks(180)).IsTrue();
        rig.Script.Repeat = null;
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Pending!.Count == 0, 10)).IsTrue();

        var worst = 0;
        foreach (var start in ticks)
        {
            worst = Math.Max(worst, ticks.Count(t => t >= start && t < start + 60));
        }

        Record($"hold_dig edits={ticks.Count} worst_window={worst} rejected={rig.Tools.Rejected}");
        AssertThat(ticks.Count).IsGreater(5);
        AssertThat(worst).IsLessEqual(5);
        AssertThat(rig.Tools.Rejected).IsEqual(0);
        AssertThat(await rig.Fixture.StopAsync()).IsTrue();
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task SoloDigBelowCore_Refused()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await ToolRig.StartSoloAsync(scene, ModeFixture.NewDirectory(), Guid.NewGuid(), lookDown: false);
        var server = rig.Composition.Server!;
        var state = server.State;
        var planet = ModeFixture.Home(state);
        var service = rig.Composition.TerrainFactory!.Terrain!.Voxel;
        var feet = new NumVector3(0f, planet.RadiusMeters - planet.CoreDepthMeters - 5f, 0f);
        AssertThat(service.TryGetPlanet(planet.Id, out var planetNode)).IsTrue();

        // A viewer deep under the surface streams in the blocks around the core limit; the server judges the dig only on loaded terrain.
        var viewer = new VoxelViewer
        {
            Position = new Vector3(feet.X, feet.Y, feet.Z),
            ViewDistance = 64,
            RequiresVisuals = false,
            RequiresCollisions = false,
        };
        planetNode!.AddChild(viewer);
        AssertThat(await rig.Fixture.WaitUntil(() => service.IsAreaLoaded(planet.Id, feet, 12f), 90)).IsTrue();
        AssertThat(state.Players.TryGet(rig.PlayerId, out var player)).IsTrue();
        var lastOp = state.LastOpId;

        // Within one call, so no server tick moves the character back before the intent is judged.
        AssertThat(server.Simulation.UpdatePlayerKinematics(rig.PlayerId, player!.Frame, player.ParentSeq, feet, player.BodyRotation, 0f)).IsTrue();
        var eye = feet + (NumVector3.UnitY * server.Simulation.Rules.EyeHeight);
        var result = server.Simulation.Submit(rig.PlayerId, new TerrainToolIntent(planet.Id, eye, -NumVector3.UnitY, 1f, TerrainEditMode.Remove, 0));

        planetNode.RemoveChild(viewer);
        viewer.QueueFree();
        AssertThat(result.Reason).IsEqual(RejectReason.BelowCore);
        AssertThat(state.LastOpId).IsEqual(lastOp);
        AssertThat(state.TerrainVersions.Count).IsEqual(0);
        AssertThat(await rig.Fixture.StopAsync()).IsTrue();
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task ToolAim_RealTerrainAdapter_DoesNotAllocate()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await ToolRig.StartSoloAsync(scene, ModeFixture.NewDirectory(), Guid.NewGuid());
        rig.Tools.InputSource = new ToolRig.ScriptedTools();
        var state = rig.Composition.Server!.State;
        var service = rig.Composition.TerrainFactory!.Terrain!.Voxel;
        var view = new GodotToolWorldView(state, service, rig.PlayerId);
        var rules = rig.Composition.Server.Simulation.Rules;
        var hello = rig.Composition.Client!.Hello!;
        var controller = new ToolController(
            rules, view, new NeverSends(), new PendingIntentTracker(NoEditPrediction.Instance, NullLogger<PendingIntentTracker>.Instance),
            BlockPalette.FromHello(hello), MaterialPalette.FromHello(hello), NullLogger<ToolController>.Instance);
        var player = rig.Player;
        var forward = NumVector3.Transform(-NumVector3.UnitZ, player.BodyRotation);
        long tick = 1_000_000;
        var frame = default(ToolInputFrame);

        for (var kind = 0; kind < 2; kind++)
        {
            controller.Update(ToolRig.Frame(select: kind == 0 ? ToolKind.TerrainManipulator : ToolKind.Builder), default, ++tick);
            var measured = long.MaxValue;
            for (var round = 0; round < 3 && measured != 0; round++)
            {
                for (var i = 0; i < 200; i++)
                {
                    AimRayBuilder.TryBuild(player.Planet, player.PredictedFeet, player.CameraOrigin, player.CameraDirection, forward, view, rules, out var warm);
                    controller.Update(frame, warm, ++tick);
                }

                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 1000; i++)
                {
                    AimRayBuilder.TryBuild(player.Planet, player.PredictedFeet, player.CameraOrigin, player.CameraDirection, forward, view, rules, out var ray);
                    controller.Update(frame, ray, ++tick);
                }

                measured = GC.GetAllocatedBytesForCurrentThread() - before;
            }

            Record($"aim_alloc tool={kind} bytes={measured} preview={controller.Preview.Kind}");
            AssertThat(measured).IsEqual(0L);
        }

        AssertThat(await rig.Fixture.StopAsync()).IsTrue();
    }

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task Solo_SaveAndRestart_KeepsTerrainAndConstructs()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var world = Guid.NewGuid();
        var rig = await ToolRig.StartSoloAsync(scene, dir, world);
        rig.Script.Press(ToolRig.Frame(primaryPressed: true, radiusSteps: 4));
        AssertThat(await rig.WaitReplies(1)).IsTrue();
        AssertThat(await rig.WaitTicks(20)).IsTrue();
        rig.Script.Press(ToolRig.Frame(secondaryPressed: true, materialSteps: 1, radiusSteps: -4));
        AssertThat(await rig.WaitReplies(2)).IsTrue();
        AssertThat(await rig.WaitTicks(20)).IsTrue();
        rig.Script.Press(ToolRig.Frame(select: ToolKind.Builder));
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Preview.Kind == ToolPreviewKind.NewConstructCell, 20)).IsTrue();
        rig.Script.Press(ToolRig.Frame(secondaryPressed: true));
        AssertThat(await rig.WaitReplies(3)).IsTrue();
        AssertThat(rig.Replies.All(r => r.Reason == RejectReason.None)).IsTrue();

        var state = rig.Composition.Server!.State;
        var service = rig.Composition.TerrainFactory!.Terrain!.Voxel;
        var terrainBefore = ModeFixture.LiveDigest(service, state);
        var constructsBefore = ConstructFingerprint(state);
        var versionsBefore = state.TerrainVersions.Count;
        AssertThat(versionsBefore).IsGreater(0);
        AssertThat(state.Constructs.Count).IsEqual(1);
        AssertThat(await rig.Fixture.StopAsync()).IsTrue();

        var second = await ToolRig.StartSoloAsync(scene, dir, world, lookDown: false);
        var state2 = second.Composition.Server!.State;
        var service2 = second.Composition.TerrainFactory!.Terrain!.Voxel;
        ulong terrainAfter = 0;
        AssertThat(await second.Fixture.WaitUntil(
            () =>
            {
                try
                {
                    terrainAfter = ModeFixture.LiveDigest(service2, state2);
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            },
            60)).IsTrue();
        Record($"restart terrain_before={terrainBefore:X16} after={terrainAfter:X16} constructs={constructsBefore}");
        AssertThat(terrainAfter).IsEqual(terrainBefore);
        AssertThat(ConstructFingerprint(state2)).IsEqual(constructsBefore);
        AssertThat(state2.TerrainVersions.Count).IsEqual(versionsBefore);
        var id = state2.Constructs.All[0].Id;
        AssertThat(await second.Fixture.WaitUntil(() => second.Composition.Constructs!.World.TryGetConstructNode(id, out _), 20)).IsTrue();
        AssertThat(await second.Fixture.StopAsync()).IsTrue();
    }

    private static string ConstructFingerprint(Horyzont.Core.World.WorldState state) =>
        string.Join(
            ";",
            state.Constructs.All.Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Id.Value}:{c.Origin}:{c.Rotation}:{c.Version}:{c.BlockCount}")));

    private static int CountOf<T>(Node node)
        where T : Node
    {
        var count = node is T ? 1 : 0;
        foreach (var child in node.GetChildren())
        {
            count += CountOf<T>(child);
        }

        return count;
    }

    private static void Record(string line)
    {
        GD.Print("    " + line);
        if (System.Environment.GetEnvironmentVariable("HORYZONT_RECON_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + "\n");
        }
    }

    private sealed class NeverSends : IIntentSender
    {
        public bool CanSend => false;

        public uint Submit(Intent intent) => throw new InvalidOperationException("The aim measurement must not send.");
    }
}
