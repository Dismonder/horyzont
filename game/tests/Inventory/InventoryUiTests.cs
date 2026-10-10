using GdUnit4;

using Godot;

using Horyzont.Client.Tools;
using Horyzont.Core.Intents;
using Horyzont.Game.Client.Inventory;
using Horyzont.Game.Client.Tools;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Tools;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Inventory;

/// <summary>K6-29W stage D: the hotbar, the inventory screen, the toasts and the tool that follows the hotbar (no graphics needed).</summary>
[TestSuite]
public class InventoryUiTests
{
    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Hotbar_HasTenSlots_HighlightsTheSelectedOne_AndNamesItFromTheCsv()
    {
        TranslationServer.SetLocale("en");
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        await scene.WaitFrames(2);

        var hud = rig.Node.GetNode<HotbarHud>("HotbarHud");
        AssertThat(hud.Slots.Count).IsEqual(10);
        AssertThat(hud.Slots.Count(s => s.IsSelected)).IsEqual(1);
        AssertThat(hud.Slots[0].IsSelected).IsTrue();
        AssertThat(hud.SelectedName).IsEqual("Terrain manipulator");

        rig.Input.Next = new InventoryInputFrame(3, 0, false, false); // key 3: the third slot
        await scene.WaitFrames(2);

        AssertThat(hud.Slots[2].IsSelected).IsTrue();
        AssertThat(hud.Slots[0].IsSelected).IsFalse();
        AssertThat(hud.SelectedName).IsEqual("Stone");
        AssertThat(hud.Slots[1].Number).IsEqual(2);
        AssertThat(hud.Slots[9].Number).IsEqual(0);

        TranslationServer.SetLocale("pl");
        rig.Node.Refresh();
        await scene.WaitFrames(2);
        AssertThat(rig.Node.ItemName(rig.Type("core:stone"))).IsEqual("Kamień");
        TranslationServer.SetLocale("en");
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task InventoryKey_OpensTheScreen_ReleasesTheMouseAndBlocksTheToolInput()
    {
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        var reader = new ToolInputReader { Blocked = () => rig.Node.IsOpen };
        scene.Add(reader);
        await scene.WaitFrames(2);
        var screen = rig.Node.GetNode<InventoryScreen>("InventoryScreen");
        AssertThat(screen.Visible).IsFalse();
        AssertThat(reader.Active).IsTrue(); // headless windows read input always

        rig.Input.Next = new InventoryInputFrame(-1, 0, true, false);
        await scene.WaitFrames(2);

        AssertThat(rig.Node.IsOpen).IsTrue();
        AssertThat(screen.Visible).IsTrue();
        AssertThat(Input.MouseMode == Input.MouseModeEnum.Captured).IsFalse();
        AssertThat(reader.Active).IsFalse();
        AssertThat(screen.BagSlots.Count).IsEqual(40);
        AssertThat(screen.HotbarSlots.Count).IsEqual(10);
        AssertThat(screen.EquipmentViews.All(s => s.Stack.IsEmpty)).IsTrue();

        rig.Input.Next = new InventoryInputFrame(-1, 0, false, true); // Escape
        await scene.WaitFrames(2);

        AssertThat(rig.Node.IsOpen).IsFalse();
        AssertThat(screen.Visible).IsFalse();
        AssertThat(reader.Active).IsTrue();
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Tooltip_ShowsTheNameFromTheCsv_AndClicksSendMoveIntents()
    {
        TranslationServer.SetLocale("en");
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        rig.Node.Open();
        await scene.WaitFrames(2);
        var screen = rig.Node.GetNode<InventoryScreen>("InventoryScreen");

        screen.HoverSlot(screen.HotbarSlots[2]);
        AssertThat(screen.Tooltip.Visible).IsTrue();
        AssertThat(screen.Tooltip.Title).IsEqual("Stone");
        AssertThat(screen.Tooltip.Body.Contains("30", StringComparison.Ordinal)).IsTrue();
        AssertThat(screen.Tooltip.Body.Contains("500", StringComparison.Ordinal)).IsTrue();

        var from = screen.HotbarSlots[2].Ref;
        var to = screen.BagSlots[5].Ref;
        rig.Node.OnSlotClicked(from, MouseButton.Left, shift: false);
        rig.Node.OnSlotClicked(to, MouseButton.Left, shift: false);

        var move = AssertIsMove(rig.Sender.Intents);
        AssertThat(move.From).IsEqual(from);
        AssertThat(move.To).IsEqual(ItemMoveTarget.ToSlot(to));
        AssertThat((int)move.Count).IsEqual(30);
        AssertThat((int)move.ExpectedCount).IsEqual(30);
        AssertThat(move.ExpectedType).IsEqual(rig.Type("core:stone"));
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Toasts_AggregateTheYield_AndWarnWhenFull()
    {
        TranslationServer.SetLocale("en");
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        var toasts = rig.Node.GetNode<PickupToasts>("PickupToasts");
        var stone = rig.Type("core:stone");

        rig.Now = 1000;
        rig.Node.Feed.OnOwnedCountChanged(stone, 5, rig.Now);
        rig.Now = 1500;
        rig.Node.Feed.OnOwnedCountChanged(stone, 7, rig.Now);
        await scene.WaitFrames(2);

        AssertThat(toasts.Texts.Count).IsEqual(1);
        AssertThat(toasts.Texts[0]).IsEqual("+12 Stone");

        rig.Node.Feed.OnDigAccepted(rig.Now, inventoryFull: true);
        rig.Now += 400;
        await scene.WaitFrames(2);

        AssertThat(toasts.Texts.Contains("Inventory full")).IsTrue();
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task SoloGame_StartKitOnTheHotbar_AndTheSelectedSlotDecidesTheTool()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await ToolRig.StartSoloAsync(scene, ModeFixture.NewDirectory(), Guid.NewGuid());
        var inventory = rig.Composition.Inventory!;
        AssertThat(await rig.Fixture.WaitUntil(() => inventory.Inventory.Hotbar is not null && inventory.Inventory.Bag is not null)).IsTrue();

        AssertThat(inventory.Inventory.Hotbar!.Size).IsEqual(10);
        AssertThat(inventory.Inventory.Bag!.Size).IsEqual(40);
        AssertThat(inventory.Inventory.KeyOf(inventory.Inventory.Hotbar.Get(0).Type)).IsEqual("core:multitool");
        AssertThat(inventory.Inventory.KeyOf(inventory.Inventory.Hotbar.Get(1).Type)).IsEqual("core:build_tool");
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Settings?.Tool == ToolKind.TerrainManipulator)).IsTrue();

        inventory.Selection.SelectKey(2);
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Settings?.Tool == ToolKind.Builder)).IsTrue();

        inventory.Selection.SelectKey(6); // an empty slot: no tool, nothing is sent
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Settings?.Tool == ToolKind.None)).IsTrue();
        rig.Script.Press(ToolRig.Frame(primaryPressed: true, primaryHeld: true));
        AssertThat(await rig.WaitTicks(30)).IsTrue();
        AssertThat(rig.Replies.Count).IsEqual(0);
        AssertThat(rig.Tools.Preview.Kind).IsEqual(ToolPreviewKind.None);

        inventory.Selection.SelectKey(1);
        AssertThat(await rig.Fixture.WaitUntil(() => rig.Tools.Settings?.Tool == ToolKind.TerrainManipulator)).IsTrue();
    }

    private static ItemMoveIntent AssertIsMove(List<Intent> intents)
    {
        AssertThat(intents.Count).IsEqual(1);
        AssertThat(intents[0] is ItemMoveIntent).IsTrue();
        return (ItemMoveIntent)intents[0];
    }
}
