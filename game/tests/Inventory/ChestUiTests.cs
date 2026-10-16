using GdUnit4;

using Godot;

using Horyzont.Client.Blocks;
using Horyzont.Client.Tools;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Client.Blocks;
using Horyzont.Game.Client.Inventory;
using Horyzont.Game.Client.Tools;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Tools;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Inventory;

/// <summary>K6-30 stage D: the chest panel of the inventory screen, the interaction key and its prompts (structure and behaviour, no graphics needed).</summary>
[TestSuite]
public class ChestUiTests
{
    private static readonly ContainerId _chestId = new(10);
    private static readonly EntityId _construct = new(100);
    private static readonly CellPos _cell = new(0, 1, 0);

    private static void FeedChest(InventoryRig rig, ContainerAccess access, ushort size = 20, params SlotContent[] slots) =>
        rig.Node.Inventory.OnMessage(new ContainerState(_chestId, RejectReason.None, ContainerKind.Chest, access, size, 1, slots));

    private static async Task<InventoryScreen> OpenChest(EngineTestScene scene, InventoryRig rig, ContainerAccess access = ContainerAccess.Shared, ushort size = 20)
    {
        rig.Node.Chests.Open(_chestId, _construct, _cell);
        FeedChest(rig, access, size, new SlotContent(1, new ItemStack(rig.Type("core:stone"), 5)));
        await scene.WaitFrames(2);
        return rig.Node.GetNode<InventoryScreen>("InventoryScreen");
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task ChestState_OpensTheScreenWithThePanel_AndEscapeUnsubscribes()
    {
        TranslationServer.SetLocale("en");
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        await scene.WaitFrames(2);
        rig.Node.Chests.Open(_chestId, _construct, _cell);
        AssertThat(rig.Node.IsOpen).IsFalse(); // nothing opens before the state of the chest arrives
        AssertThat(rig.Sent.Contains(new ContainerSubscribe(_chestId))).IsTrue();

        FeedChest(rig, ContainerAccess.Shared, 20, new SlotContent(1, new ItemStack(rig.Type("core:stone"), 5)));
        await scene.WaitFrames(2);

        var screen = rig.Node.GetNode<InventoryScreen>("InventoryScreen");
        AssertThat(rig.Node.IsOpen).IsTrue();
        AssertThat(screen.Visible).IsTrue();
        AssertThat(Input.MouseMode == Input.MouseModeEnum.Captured).IsFalse();
        AssertThat(screen.Chest!.Visible).IsTrue();
        AssertThat(screen.Chest.Slots.Count(s => s.Visible)).IsEqual(20);
        AssertThat(screen.Chest.Slots[1].Stack).IsEqual(new ItemStack(rig.Type("core:stone"), 5));
        AssertThat(screen.Chest.TitleText).IsEqual("Chest");
        AssertThat(screen.Chest.AccessText).IsEqual("Shared - everyone may use it");
        AssertThat(screen.BagSlots.Count).IsEqual(40);
        AssertThat(screen.QuickStackButton!.Visible).IsFalse(); // the panel has its own button

        rig.Input.Next = new InventoryInputFrame(-1, 0, false, true); // Escape
        await scene.WaitFrames(2);

        AssertThat(rig.Node.IsOpen).IsFalse();
        AssertThat(screen.Chest.IsVisibleInTree()).IsFalse();
        AssertThat(rig.Node.Chests.IsOpen).IsFalse();
        AssertThat(rig.Sent.Contains(new ContainerUnsubscribe(_chestId))).IsTrue();
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task InventoryKey_WithAChestOpen_ClosesBoth()
    {
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        await OpenChest(scene, rig);

        rig.Input.Next = new InventoryInputFrame(-1, 0, true, false); // Tab
        await scene.WaitFrames(2);

        AssertThat(rig.Node.IsOpen).IsFalse();
        AssertThat(rig.Node.Chests.IsOpen).IsFalse();
        AssertThat(rig.Sent.Contains(new ContainerUnsubscribe(_chestId))).IsTrue();
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task ServerEndsTheSubscription_ClosesTheScreenWithoutUnsubscribing()
    {
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        await OpenChest(scene, rig);

        rig.Node.Inventory.OnMessage(ContainerState.Rejected(_chestId, RejectReason.OutOfReach));
        await scene.WaitFrames(2);

        AssertThat(rig.Node.IsOpen).IsFalse();
        AssertThat(rig.Node.Chests.IsOpen).IsFalse();
        AssertThat(rig.Sent.Contains(new ContainerUnsubscribe(_chestId))).IsFalse();
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task ClicksBetweenTheHotbarAndTheChest_SendMoveIntents()
    {
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        var screen = await OpenChest(scene, rig);

        var from = screen.HotbarSlots[2].Ref; // 30 stone
        var to = screen.Chest!.Slots[3].Ref;
        rig.Node.OnSlotClicked(from, MouseButton.Left, shift: false);
        rig.Node.OnSlotClicked(to, MouseButton.Left, shift: false);

        var move = rig.Sender.Intents.OfType<ItemMoveIntent>().Single();
        AssertThat(move.From).IsEqual(from);
        AssertThat(move.To).IsEqual(ItemMoveTarget.ToSlot(to));
        AssertThat((int)move.Count).IsEqual(30);

        rig.Sender.Intents.Clear();
        rig.Node.OnSlotClicked(screen.Chest.Slots[1].Ref, MouseButton.Left, shift: true); // Shift+click in the chest: any place of the bag
        var shift = rig.Sender.Intents.OfType<ItemMoveIntent>().Single();
        AssertThat(shift.To).IsEqual(ItemMoveTarget.AnyIn(new ContainerId(InventoryRig.BagId)));
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task PanelButtons_SendTheTakeAllAndQuickStackIntents()
    {
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        var screen = await OpenChest(scene, rig);

        screen.Chest!.PressTakeAll();
        screen.Chest.PressQuickStack();

        AssertThat(rig.Sender.Intents.Count).IsEqual(2);
        AssertThat(rig.Sender.Intents[0]).IsEqual(new TakeAllIntent(_chestId));
        AssertThat(rig.Sender.Intents[1]).IsEqual(new QuickStackIntent());
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task QuickStackButton_WithoutAChest_SendsTheIntent()
    {
        TranslationServer.SetLocale("en");
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        rig.Node.Open();
        await scene.WaitFrames(2);
        var screen = rig.Node.GetNode<InventoryScreen>("InventoryScreen");

        AssertThat(screen.Chest!.Visible).IsFalse();
        AssertThat(screen.QuickStackButton!.Visible).IsTrue();
        AssertThat(screen.QuickStackButton.Text).IsEqual("Put matching into nearby chests");
        screen.QuickStackButton.EmitSignal(BaseButton.SignalName.Pressed);

        AssertThat(rig.Sender.Intents.Single()).IsEqual(new QuickStackIntent());
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task AccessSwitch_IsOfferedUntilTheServerSaysNotOwner()
    {
        TranslationServer.SetLocale("en");
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        var screen = await OpenChest(scene, rig, ContainerAccess.Private);

        AssertThat(screen.Chest!.AccessSwitchVisible).IsTrue();
        AssertThat(screen.Chest.AccessSwitchText).IsEqual("Make shared");
        AssertThat(screen.Chest.AccessText).IsEqual("Private - only you");

        screen.Chest.PressAccessSwitch();
        var config = rig.Sender.Intents.OfType<BlockConfigIntent>().Single();
        AssertThat(config).IsEqual(new BlockConfigIntent(_construct, _cell, new ContainerAccessConfig(ContainerAccess.Shared)));

        rig.Node.Chests.OnIntentReply(new IntentReply(901, RejectReason.NotOwner, default));
        await scene.WaitFrames(2);

        AssertThat(screen.Chest.AccessSwitchVisible).IsFalse();
        AssertThat(screen.RefusalText).IsEqual("Not yours");
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Texts_ExistInBothLanguages()
    {
        await using var scene = EngineTestScene.Create();
        _ = scene;
        var keys = Enum.GetValues<BlockPromptKind>().Select(BlockPromptHud.KeyOf)
            .Concat(["REJECT_NOT_OWNER", "REJECT_CONTAINER_NOT_EMPTY", "INV_CHEST", "INV_CHEST_SHARED", "INV_CHEST_PRIVATE", "INV_ACCESS_TO_PRIVATE",
                     "INV_ACCESS_TO_SHARED", "INV_TAKE_ALL", "INV_QUICK_STACK", "INV_QUICK_STACK_NEARBY"]);
        foreach (var locale in new[] { "en", "pl" })
        {
            TranslationServer.SetLocale(locale);
            foreach (var key in keys)
            {
                var text = TranslationServer.Translate(key).ToString();
                AssertThat(text).IsNotEqual(key);
                AssertThat(text.Length).IsGreater(0);
            }
        }

        TranslationServer.SetLocale("en");
        AssertThat(TranslationServer.Translate(BlockPromptHud.KeyOf(BlockPromptKind.OpenDoor)).ToString()).IsEqual("[E] Open door");
        TranslationServer.SetLocale("pl");
        AssertThat(TranslationServer.Translate(BlockPromptHud.KeyOf(BlockPromptKind.OwnBed)).ToString()).IsEqual("Twoje łóżko");
        TranslationServer.SetLocale("en");
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task InteractionKey_IsE_AndTheReaderCountsAPress()
    {
        await using var scene = EngineTestScene.Create();
        var reader = new ToolInputReader();
        scene.Add(reader);
        await scene.WaitFrames(2);

        AssertThat(InputMap.HasAction("block_interact")).IsTrue();
        AssertThat(InputMap.ActionGetEvents("block_interact").OfType<InputEventKey>().Any(e => e.PhysicalKeycode == Key.E)).IsTrue();

        AssertThat(reader.Take().BlockInteract).IsFalse();
        reader._Input(new InputEventAction { Action = "block_interact", Pressed = true });
        AssertThat(reader.Take().BlockInteract).IsTrue();
        AssertThat(reader.Take().BlockInteract).IsFalse(); // handed over once
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task SoloGame_InteractionKeyOnADoor_OpensItOnTheServer()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await ToolRig.StartSoloAsync(scene, ModeFixture.NewDirectory(), Guid.NewGuid());
        var server = rig.Composition.Server!;
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Interaction is not null)).IsTrue();

        // A one-block door construct around the point the crosshair meets the ground, so the eye ray reaches it first.
        var state = server.State;
        AssertThat(state.BlockTypes.TryGetId("core:wood_door", out var doorType)).IsTrue();
        var id = state.EntityIds.PeekNext();
        var cell = new CellPos(0, 0, 0);
        server.Simulation.ApplyServerOperation(new ConstructCreateOp(
            id, FrameRef.Of(state.HomePlanet), rig.Tools.Preview.Center - new System.Numerics.Vector3(0.5f, 0.5f, 0.5f), System.Numerics.Quaternion.Identity,
            cell, BlockState.Create(doorType, 0)));

        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Interaction!.Prompt?.Kind == BlockPromptKind.OpenDoor)).IsTrue();

        rig.Script.Press(new ToolInputFrame(false, false, false, false, null, 0, 0, 0, 0, 0, false, BlockInteract: true));
        AssertThat(await rig.Fixture.WaitUntil(() => state.Constructs.TryGet(id, out var door) && door.GetBlock(cell).FunctionState == 1)).IsTrue();
        AssertThat(rig.Tools.LastBlockInteraction).IsEqual(BlockInteractionResult.Sent);
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Interaction!.Prompt?.Kind == BlockPromptKind.CloseDoor)).IsTrue();
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task SoloGame_InteractionKeyOnAChest_OpensTheScreenWithItsPanel()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await ToolRig.StartSoloAsync(scene, ModeFixture.NewDirectory(), Guid.NewGuid());
        var server = rig.Composition.Server!;
        var inventory = rig.Composition.Inventory!;
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Interaction is not null && inventory.Inventory.Bag is not null)).IsTrue();

        var state = server.State;
        AssertThat(state.BlockTypes.TryGetId("core:chest_wood", out var chestType)).IsTrue();
        var construct = state.EntityIds.PeekNext();
        var container = state.ContainerIds.PeekNext();
        var cell = new CellPos(0, 0, 0);
        server.Simulation.ApplyServerOperation(new CompoundOp(
        [
            new ContainerCreateOp(container, ContainerKind.Chest, ContainerHost.ConstructBlock(construct, cell), rig.PlayerId, ContainerAccess.Shared, 20),
            new BlockEntitySetOp(construct, cell, chestType, new ChestBlockState(container)),
            new ConstructCreateOp(
                construct, FrameRef.Of(state.HomePlanet), rig.Tools.Preview.Center - new System.Numerics.Vector3(0.5f, 0.5f, 0.5f),
                System.Numerics.Quaternion.Identity, cell, BlockState.Create(chestType, 0)),
        ]));

        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Interaction!.Prompt?.Kind == BlockPromptKind.OpenChest)).IsTrue();

        rig.Script.Press(new ToolInputFrame(false, false, false, false, null, 0, 0, 0, 0, 0, false, BlockInteract: true));
        AssertThat(await rig.Fixture.WaitUntil(() => inventory.Chests.IsOpen && inventory.IsOpen)).IsTrue();
        AssertThat(inventory.Chests.OpenContainer!.Size).IsEqual(20);

        inventory.Close();
        AssertThat(inventory.Chests.IsOpen).IsFalse();
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task PromptHud_ShowsNothingWithoutAnAimedBlock()
    {
        await using var scene = EngineTestScene.Create();
        var tools = new ToolNode();
        var hud = new BlockPromptHud(tools);
        scene.Add(hud);
        await scene.WaitFrames(2);

        AssertThat(hud.Text).IsEqual(string.Empty);
        tools.Free();
    }
}
