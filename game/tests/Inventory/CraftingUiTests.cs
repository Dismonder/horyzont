using GdUnit4;

using Godot;

using Horyzont.Client.Blocks;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Client.Blocks;
using Horyzont.Game.Client.Inventory;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Inventory;

/// <summary>
/// K6-32 stage D: the crafting panel and the panel of a furnace on the inventory screen (structure and behaviour, no graphics needed).
/// The look is not judged here: it awaits the assessment of the owner.
/// </summary>
[TestSuite]
public class CraftingUiTests
{
    private static readonly EntityId _construct = new(100);
    private static readonly CellPos _cell = new(4, 1, 0);
    private static readonly BlockRef _station = new(_construct, _cell);
    private static readonly ContainerId _input = new(20);
    private static readonly ContainerId _output = new(21);

    private static bool NoTick(out ulong tick)
    {
        tick = 0;
        return false;
    }

    /// <summary>Tells the node where the state of the furnace block is (the station session reads it from there).</summary>
    private static void ReplicateFurnace(InventoryRig rig, string recipe = "", Tick? started = null)
    {
        var entities = new ClientBlockEntities();
        entities.Apply(new BlockEntityStates(_construct, [new BlockEntityEntry(_cell, new StationBlockState(_input, _output, recipe, started))]));
        rig.Node.ConfigureStations(entities, NoTick);
    }

    private static void Know(InventoryRig rig, params string[] discovered) =>
        rig.Node.Progress.Apply(
            new PlayerProgressState(discovered.Aggregate(ItemTypeBitSet.Empty, (set, key) => set.With(rig.Type(key))), [], FoodSlots.Empty));

    private static void FeedStation(InventoryRig rig, params SlotContent[] output)
    {
        rig.Node.Inventory.OnMessage(new ContainerState(_input, RejectReason.None, ContainerKind.StationInput, ContainerAccess.Shared, 4, 1, []));
        rig.Node.Inventory.OnMessage(new ContainerState(_output, RejectReason.None, ContainerKind.StationOutput, ContainerAccess.Shared, 4, 1, output));
    }

    private static async Task<InventoryScreen> OpenFurnace(EngineTestScene scene, InventoryRig rig)
    {
        ReplicateFurnace(rig);
        AssertThat(rig.Node.Stations.Open(StationKind.Furnace, _station)).IsTrue();
        FeedStation(rig, new SlotContent(0, new ItemStack(rig.Type("core:iron_ingot"), 2)));
        await scene.WaitFrames(2);
        return rig.Node.GetNode<InventoryScreen>("InventoryScreen");
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task FurnaceStates_OpenTheScreenWithTheStationPanel_AndEscapeUnsubscribesBoth()
    {
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        await scene.WaitFrames(2);
        ReplicateFurnace(rig);

        AssertThat(rig.Node.Stations.Open(StationKind.Furnace, _station)).IsTrue();
        AssertThat(rig.Sent.Contains(new ContainerSubscribe(_input))).IsTrue();
        AssertThat(rig.Sent.Contains(new ContainerSubscribe(_output))).IsTrue();
        AssertThat(rig.Node.IsOpen).IsFalse(); // nothing opens before both states arrive

        FeedStation(rig, new SlotContent(0, new ItemStack(rig.Type("core:iron_ingot"), 2)));
        await scene.WaitFrames(2);

        var screen = rig.Node.GetNode<InventoryScreen>("InventoryScreen");
        AssertThat(rig.Node.IsOpen).IsTrue();
        AssertThat(screen.Visible).IsTrue();
        AssertThat(Input.MouseMode == Input.MouseModeEnum.Captured).IsFalse();
        AssertThat(screen.Station!.Visible).IsTrue();
        AssertThat(screen.Station.InputSlots.Count(s => s.Visible)).IsEqual(4);
        AssertThat(screen.Station.OutputSlots.Count(s => s.Visible)).IsEqual(4);
        AssertThat(screen.Station.OutputSlots[0].Stack).IsEqual(new ItemStack(rig.Type("core:iron_ingot"), 2));
        AssertThat(screen.Crafting!.Visible).IsFalse(); // the station takes the place of the crafting list
        AssertThat(screen.Chest!.Visible).IsFalse();
        AssertThat(screen.QuickStackButton!.Visible).IsFalse();

        rig.Input.Next = new InventoryInputFrame(-1, 0, false, true); // Escape
        await scene.WaitFrames(2);

        AssertThat(rig.Node.IsOpen).IsFalse();
        AssertThat(rig.Node.Stations.IsOpen).IsFalse();
        AssertThat(screen.Station.IsVisibleInTree()).IsFalse();
        AssertThat(rig.Sent.Contains(new ContainerUnsubscribe(_input))).IsTrue();
        AssertThat(rig.Sent.Contains(new ContainerUnsubscribe(_output))).IsTrue();
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task ServerEndsTheStationSubscription_ClosesTheScreen()
    {
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        _ = await OpenFurnace(scene, rig);

        rig.Node.Inventory.OnMessage(ContainerState.Rejected(_output, RejectReason.OutOfReach));
        await scene.WaitFrames(2);

        AssertThat(rig.Node.IsOpen).IsFalse();
        AssertThat(rig.Node.Stations.IsOpen).IsFalse();
        AssertThat(rig.Node.Inventory.TryGetContainer(_input, out _)).IsFalse();
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task FurnacePanel_ChooseAndTakeAll_SendIntents()
    {
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        Know(rig, "core:ore", "core:sand");
        var screen = await OpenFurnace(scene, rig);

        AssertThat(screen.Station!.ChooseCount).IsEqual(2); // iron ingot (ore) and glass (sand)
        AssertThat(screen.Station.RecipeKey(0)).IsEqual("core:iron_ingot");

        screen.Station.PressChoose(0);
        screen.Station.PressTakeAll();

        AssertThat(rig.Sender.Intents.OfType<CraftIntent>().Single()).IsEqual(new CraftIntent("core:iron_ingot", 1, _station));
        AssertThat(rig.Sender.Intents.OfType<TakeAllIntent>().Single()).IsEqual(new TakeAllIntent(_output));
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task HandsPanel_ShowsKnownRecipes_MakeSendsTheCountWithoutAStation()
    {
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        await scene.WaitFrames(2);
        rig.Input.Next = new InventoryInputFrame(-1, 0, true, false); // Tab
        await scene.WaitFrames(2);
        var screen = rig.Node.GetNode<InventoryScreen>("InventoryScreen");

        AssertThat(screen.Crafting!.Visible).IsTrue();
        AssertThat(screen.Crafting.RowCount).IsEqual(0); // nothing discovered yet

        Know(rig, "core:soil");
        await scene.WaitFrames(2);

        AssertThat(screen.Crafting.RowCount).IsEqual(1); // peat from soil
        screen.Crafting.SetCount(0, 3);
        screen.Crafting.PressMake(0);

        AssertThat(rig.Sender.Intents.OfType<CraftIntent>().Single()).IsEqual(new CraftIntent("core:peat", 3, null));
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task WorkbenchOpened_OpensTheScreenWithItsRecipes_MakeSendsTheStation()
    {
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        Know(rig, "core:iron_ingot");
        await scene.WaitFrames(2);

        rig.Node.Crafting.OpenStation(StationKind.Workbench, _station);
        await scene.WaitFrames(2);

        var screen = rig.Node.GetNode<InventoryScreen>("InventoryScreen");
        AssertThat(rig.Node.IsOpen).IsTrue();
        AssertThat(screen.Crafting!.Visible).IsTrue();
        var keys = Enumerable.Range(0, screen.Crafting.RowCount).Select(screen.Crafting.RecipeKey).ToList();
        AssertThat(keys).Contains("core:multitool_t2"); // the tool of the second tier; the suit parts that use an iron ingot may share the list
        screen.Crafting.PressMake(keys.IndexOf("core:multitool_t2"));

        AssertThat(rig.Sender.Intents.OfType<CraftIntent>().Single()).IsEqual(new CraftIntent("core:multitool_t2", 1, _station));

        rig.Input.Next = new InventoryInputFrame(-1, 0, false, true); // Escape
        await scene.WaitFrames(2);

        AssertThat(rig.Node.IsOpen).IsFalse();
        AssertThat(rig.Node.Crafting.IsStationOpen).IsFalse();
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Texts_ExistInBothLanguages()
    {
        await using var scene = EngineTestScene.Create();
        _ = scene;
        string[] keys =
        [
            "CRAFT_TITLE_HANDS", "CRAFT_TITLE_WORKBENCH", "CRAFT_TITLE_KITCHEN", "CRAFT_TITLE_FABRICATOR", "CRAFT_TITLE_FURNACE",
            "CRAFT_TITLE_REFINERY", "CRAFT_MAKE", "CRAFT_NONE", "CRAFT_LOCKED", "CRAFT_LEVEL", "CRAFT_NEARBY", "CRAFT_SELECT", "CRAFT_ACTIVE",
            "CRAFT_NO_RECIPE", "CRAFT_INPUT", "CRAFT_OUTPUT", "CRAFT_FUEL", "REJECT_STATION_REQUIRED", "REJECT_RECIPE_LOCKED",
            "REJECT_TOO_FAR_FROM_STATION", "PROMPT_STATION_OPEN", "PROMPT_STATION_WORKBENCH", "PROMPT_STATION_FURNACE", "PROMPT_STATION_REFINERY",
            "PROMPT_STATION_KITCHEN", "PROMPT_STATION_FABRICATOR", "ITEM_CORE_PEAT", "ITEM_CORE_STONE_BRICK", "ITEM_CORE_IRON_INGOT",
            "ITEM_CORE_GLASS", "ITEM_CORE_FUEL_CELL", "ITEM_CORE_MULTITOOL_T2",
        ];
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
        AssertThat(TranslationServer.Translate("CRAFT_MAKE").ToString()).IsEqual("Make");
        AssertThat(TranslationServer.Translate(BlockPromptHud.KeyOfPrompt(new BlockPrompt(BlockPromptKind.OpenStation, _construct, _cell, StationKind.Furnace))).ToString())
            .IsEqual("[E] Furnace");
        TranslationServer.SetLocale("pl");
        AssertThat(TranslationServer.Translate("CRAFT_MAKE").ToString()).IsEqual("Zrób");
        AssertThat(TranslationServer.Translate("REJECT_TOO_FAR_FROM_STATION").ToString()).IsEqual("Za daleko od stacji");
        TranslationServer.SetLocale("en");
        await Task.CompletedTask;
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Panels_ShowTranslatedTexts()
    {
        TranslationServer.SetLocale("en");
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        Know(rig, "core:ore");
        var screen = await OpenFurnace(scene, rig);

        AssertThat(screen.Station!.TitleText).IsEqual("Furnace");
        AssertThat(screen.Station.ActiveText).IsEqual("No recipe chosen");

        rig.Node.Inventory.OnMessage(ContainerState.Rejected(_output, RejectReason.OutOfReach));
        rig.Node.Crafting.OpenStation(StationKind.Workbench, _station);
        Know(rig, "core:iron_ingot");
        await scene.WaitFrames(2);

        AssertThat(screen.Crafting!.TitleText).IsEqual("Workbench");
        AssertThat(screen.Crafting.RowText(0)).Contains("Terrain manipulator II x1");
        AssertThat(screen.Crafting.RowText(0)).Contains("Needs level 2");
        AssertThat(screen.Crafting.MakeButton(0).Text).IsEqual("Make");
        TranslationServer.SetLocale("pl");
        rig.Node.Refresh();
        await scene.WaitFrames(2);
        AssertThat(screen.Crafting.TitleText).IsEqual("Warsztat");
        AssertThat(screen.Crafting.MakeButton(0).Text).IsEqual("Zrób");
        TranslationServer.SetLocale("en");
    }

    [TestCase]
    public void StationPrompt_NamesTheStation()
    {
        var furnace = new BlockPrompt(BlockPromptKind.OpenStation, _construct, _cell, StationKind.Furnace);

        AssertThat(BlockPromptHud.KeyOfPrompt(furnace)).IsEqual("PROMPT_STATION_FURNACE");
        AssertThat(BlockPromptHud.KeyOfPrompt(furnace with { Station = StationKind.Workbench })).IsEqual("PROMPT_STATION_WORKBENCH");
        AssertThat(BlockPromptHud.KeyOfPrompt(new BlockPrompt(BlockPromptKind.OpenChest, _construct, _cell))).IsEqual("PROMPT_OPEN_CHEST");
    }
}
