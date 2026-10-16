using System.Globalization;

using GdUnit4;

using Horyzont.Client.Props;
using Horyzont.Client.Tools;
using Horyzont.Core.Intents;
using Horyzont.Core.Motion;
using Horyzont.Core.Props;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Boot;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Client.Tools;
using Horyzont.Game.Planet.Props;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Tools;

using static GdUnit4.Assertions;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Props;

/// <summary>
/// K6-31 D5, no window: the hand and the multitool hit the props of the layout V1 in a real solo game (and host game) with the real
/// terrain, the real server and the dummy renderer of a headless run. The player is put next to a prop the renderer drew and looks at
/// its foot; three presses of the hand break a rock, and the rock disappears from the drawing, the stone arrives in the inventory.
/// </summary>
[TestSuite]
public class PropsSoloTests
{
    private const string StoneKey = "core:stone";

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task Solo_HandOnRock_ThreeHits_RockGoneFromDrawing_StoneInInventory()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await Rig.StartAsync(scene, host: false);
        await HitRockWithTheHand(rig);
        AssertThat(await rig.Fixture.StopAsync()).IsTrue();
    }

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task Host_HandOnRock_ThreeHits_RockGoneFromDrawing_StoneInInventory()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await Rig.StartAsync(scene, host: true);
        await HitRockWithTheHand(rig);
        AssertThat(await rig.Fixture.StopAsync()).IsTrue();
    }

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task Solo_HandOnTree_PromptsTheTier_SendsNothing()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await Rig.StartAsync(scene, host: false);
        var tree = await rig.AimAtAsync(p => p.Kind is PropKindV1.Pine or PropKindV1.PineTall or PropKindV1.Broadleaf or PropKindV1.BroadleafAutumn);
        AssertThat(tree is not null).IsTrue();
        AssertThat(rig.Tools.Props!.Prompt!.Value.Kind).IsEqual(PropPromptKind.ToolTierTooLow);
        AssertThat(rig.Tools.Props.Prompt!.Value.RequiredTier).IsEqual(1);

        // Holding the button for a second: the hand is too weak, so nothing is sent, nothing is dug (the hand has no terrain tool) and the tree stands.
        var versions = rig.Composition.Server!.State.TerrainVersions.Count;
        rig.Script.Repeat = () => ToolRig.Frame(primaryHeld: true);
        await rig.Fixture.WaitUntil(() => false, 1.0);
        rig.Script.Repeat = null;
        AssertThat(rig.Replies.Count).IsEqual(0);
        AssertThat(rig.Composition.Server.State.TerrainVersions.Count).IsEqual(versions);
        AssertThat(rig.Composition.Server.State.HarvestedProps.Contains(rig.Player.Planet, tree!.Value.Id)).IsFalse();
        AssertThat(await rig.Fixture.StopAsync()).IsTrue();
    }

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task Solo_MultitoolOnRock_HitsTheRockAndDoesNotDig()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await Rig.StartAsync(scene, host: false);
        rig.Composition.Inventory!.Selection.SelectSlot(0); // the multitool of the start kit
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Settings?.Tool == ToolKind.TerrainManipulator, 10)).IsTrue();
        var rock = await rig.AimAtAsync(p => p.Kind == PropKindV1.Rock);
        AssertThat(rock is not null).IsTrue();
        var versions = rig.Composition.Server!.State.TerrainVersions.Count;
        rig.Script.Repeat = () => ToolRig.Frame(primaryHeld: true);
        var gone = await rig.Fixture.WaitUntil(() => rig.Composition.Server.State.HarvestedProps.Contains(rig.Player.Planet, rock!.Value.Id), 30);
        rig.Script.Repeat = null;
        AssertThat(gone).IsTrue();

        // Tier 1 on a tier 0 rock deals 2 damage per hit: two hits, not three. And the ground under it was not dug.
        AssertThat(rig.Replies.Count(r => r.Reason == RejectReason.None) >= 2).IsTrue();
        AssertThat(rig.Composition.Server.State.TerrainVersions.Count).IsEqual(versions);
        AssertThat(await rig.Fixture.StopAsync()).IsTrue();
    }

    private static async Task HitRockWithTheHand(Rig rig)
    {
        var state = rig.Composition.Server!.State;
        var planet = rig.Player.Planet;
        // The server runs in this process, so the client reads the world itself (D18), not messages.
        AssertThat(rig.Composition.PropStates is SharedPropStates).IsTrue();
        var rock = await rig.AimAtAsync(p => p.Kind == PropKindV1.Rock);
        AssertThat(rock is not null).IsTrue();
        AssertThat(rig.Tools.Props!.Prompt!.Value.Kind).IsEqual(PropPromptKind.BreakRock);
        var id = rock!.Value.Id;
        var region = id.Region;
        var before = rig.Renderer.InstanceCount(planet, region);
        AssertThat(before > 0).IsTrue();
        AssertThat(rig.Composition.PropStates!.IsHarvested(planet, id)).IsFalse();
        var stone = rig.StoneCount();

        // Hold the button: three hits at four a second.
        rig.Script.Repeat = () => ToolRig.Frame(primaryHeld: true);
        AssertThat(await rig.Fixture.WaitUntil(() => state.HarvestedProps.Contains(planet, id), 30)).IsTrue();
        rig.Script.Repeat = null;

        // The rock is a world fact now; the view of the client, the drawing and the inventory follow.
        AssertThat(rig.Composition.PropStates.IsHarvested(planet, id)).IsTrue();
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Renderer.InstanceCount(planet, region) == before - 1, 20)).IsTrue();
        AssertThat(await rig.Fixture.WaitUntil(() => rig.StoneCount() == stone + 2, 20)).IsTrue();

        // Two hits were accepted without an operation, the third carried the harvest.
        AssertThat(rig.Replies.Count).IsEqual(3);
        AssertThat(rig.Replies[0].Reason).IsEqual(RejectReason.None);
        AssertThat(rig.Replies[0].AppliedOpId.Value).IsEqual(0UL);
        AssertThat(rig.Replies[1].AppliedOpId.Value).IsEqual(0UL);
        AssertThat(rig.Replies[2].AppliedOpId.Value > 0UL).IsTrue();
    }

    private sealed class Rig
    {
        private Rig(ModeFixture fixture)
        {
            Fixture = fixture;
            Composition = fixture.Composition;
            Tools = Composition.Tools!;
            Player = Composition.LocalPlayer!;
            Renderer = Composition.PropRenderer!;
            Tools.InputSource = Script;
        }

        public ModeFixture Fixture { get; }

        public GameComposition Composition { get; }

        public ToolNode Tools { get; }

        public LocalPlayer Player { get; }

        public PropRenderer Renderer { get; }

        public ToolRig.ScriptedTools Script { get; } = new();

        public List<IntentReply> Replies { get; } = [];

        public static async Task<Rig> StartAsync(EngineTestScene scene, bool host)
        {
            var dir = ModeFixture.NewDirectory();
            var common = new[] { "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString() };
            var mode = host
                ? new[] { "--host", "--bind", "127.0.0.1", "--port", ModeFixture.NextPort().ToString(CultureInfo.InvariantCulture) }
                : new[] { "--solo" };
            var fixture = await ModeFixture.StartRenderedAsync(scene, dir, false, [.. mode, .. common]);
            if (fixture.StartExit is not null || !await fixture.WaitInGame() || !await fixture.WaitUntil(() => fixture.Composition.LocalPlayer!.IsActive, 120))
            {
                throw new InvalidOperationException("The game did not reach the playing state.");
            }

            var rig = new Rig(fixture);
            if (!await fixture.WaitTerrainIdle())
            {
                throw new InvalidOperationException("The terrain did not settle.");
            }

            // Look at the ground a couple of metres ahead (the foot of a prop 2 m away), once; the pitch stays.
            var pitched = false;
            rig.Player.InputSource = new ToolRig.Scripted(_ =>
            {
                if (pitched)
                {
                    return LocalInputSample.None;
                }

                pitched = true;
                return new LocalInputSample(0f, 0f, InputButtons.None, 0f, LookAtFootRadians);
            });

            // The hand: the empty last slot of the hotbar (the multitool of the start kit is in the first).
            rig.Composition.Inventory!.Selection.SelectSlot(9);
            rig.Tools.IntentAnswered += (reply, _) => rig.Replies.Add(reply);
            if (!await fixture.WaitUntil(() => rig.Renderer.BuiltRegions >= 30 && rig.Renderer.PendingJobs == 0, 120)
                || !await fixture.WaitUntil(() => rig.Tools.Controller is not null, 30))
            {
                throw new InvalidOperationException("The props were not drawn.");
            }

            return rig;
        }

        /// <summary>Pitch that puts the eye ray through the foot of a prop 2 m ahead (the eye is about 1.6 m up).</summary>
        public const float LookAtFootRadians = -0.58f;

        public int StoneCount() =>
            Composition.Inventory!.Inventory.TryGetTypeId(StoneKey, out var type) ? Composition.Inventory.Inventory.CountOf(type) : 0;

        /// <summary>
        /// Puts the player 2 m in front of a prop that fits the predicate, facing it, and waits for the hint; tries the nearest candidates in
        /// turn (a slope or a neighbour may hide the foot of one). Returns the prop that is aimed at, or null.
        /// </summary>
        public async Task<PropInstance?> AimAtAsync(Func<PropInstance, bool> fits)
        {
            var planet = Player.Planet;
            var found = new List<PropInstance>();
            Renderer.CollectInSphere(planet, Player.PredictedFeet, 90f, found);
            var states = Composition.PropStates!;
            var candidates = found.Where(p => fits(p) && !states.IsHarvested(planet, p.Id))
                .OrderBy(p => NumVector3.Distance(p.Anchor, Player.PredictedFeet)).Take(15).ToList();
            var characters = Composition.ServerCharacters!;
            var self = Composition.Client!.Hello!.YourPlayer;
            foreach (var prop in candidates)
            {
                // Face the prop: the camera direction is kept (the yaw is the client's), the player stands 2 m before the prop along it.
                var up = NumVector3.Normalize(prop.Anchor);
                var forward = Player.CameraDirection - (up * NumVector3.Dot(Player.CameraDirection, up));
                if (forward.LengthSquared() < 1e-4f)
                {
                    continue;
                }

                forward = NumVector3.Normalize(forward);
                var direction = NumVector3.Normalize(prop.Anchor - (forward * 2f));
                if (!Composition.TerrainFactory!.Terrain!.Service.TrySampleGeneratedSurface(planet, direction, out var radius))
                {
                    continue;
                }

                var body = characters.GetNode<Horyzont.Game.Character.PlayerBody>($"Body_{self.Value}");
                body.Place(direction * (radius + 0.3f), NumVector3.Zero, body.BodyRotation, MovementMode.Walking, false);
                var tick = Tools.Ticks + 60;
                await Fixture.WaitUntil(() => Tools.Ticks >= tick, 20);
                if (Tools.Props?.Prompt is { } prompt && Tools.Props.HasTarget && NumVector3.Distance(Player.PredictedFeet, prop.Anchor) < 3.5f
                    && prompt.Prop == prop.Kind)
                {
                    return prop;
                }
            }

            return null;
        }
    }
}
