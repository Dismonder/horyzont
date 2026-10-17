using GdUnit4;

using Godot;

using Horyzont.Client.Blocks;
using Horyzont.Client.Survival;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Survival;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Client.Inventory;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Inventory;

/// <summary>The equipment slots, the suit rows and the generator panel of the inventory screen (K6-34 D3), without a window; how they look is for the owner to judge.</summary>
[TestSuite]
public class SuitPanelTests
{
    private static readonly ContainerId _equipmentId = new(3);
    private static readonly ContainerId _generatorInput = new(20);
    private static readonly EntityId _construct = new(100);
    private static readonly CellPos _cell = new(0, 1, 0);

    private static void FeedEquipment(InventoryRig rig) =>
        rig.Node.Inventory.OnMessage(
            new ContainerState(
                _equipmentId, RejectReason.None, ContainerKind.Equipment, ContainerAccess.Private, 7, 1,
                [new SlotContent(4, new ItemStack(rig.Type("core:module_o2_tank"), 1))]));

    private static void Told(InventoryRig rig) =>
        rig.Node.Vitals!.Apply(
            new PlayerVitalsMessage(
                100f, 100f, 100f, 100f, false, false, false, 0, 0, 0, VitalsEnvironment.None,
                new SuitResources(100f, 100f, 100f, 100f, 75f), new SuitResources(100f, 100f, 100f, 100f, 200f), default, 0), 1000);

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task EquipmentSlotsEnabled_BackpackDimmed_SuitPanelRows()
    {
        TranslationServer.SetLocale("en");
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        rig.Node.Vitals = new ClientVitals();
        await scene.WaitFrames(2);
        var screen = rig.Node.GetNode<InventoryScreen>("InventoryScreen");

        // A world without hazards: greyed placeholders, the old "coming soon" line and no suit rows.
        rig.Input.Next = new InventoryInputFrame(-1, 0, true, false);
        await scene.WaitFrames(2);
        AssertThat(screen.SoonLabel!.Visible).IsTrue();
        AssertThat(screen.Suit!.Visible).IsFalse();

        rig.Input.Next = new InventoryInputFrame(-1, 0, false, true);
        await scene.WaitFrames(2);
        rig.Node.Interaction.EquipmentEnabled = true;
        FeedEquipment(rig);
        Told(rig);
        rig.Input.Next = new InventoryInputFrame(-1, 0, true, false);
        await scene.WaitFrames(3);

        AssertThat(screen.SoonLabel.Visible).IsFalse();
        AssertThat(screen.Suit.Visible).IsTrue();
        AssertThat(screen.EquipmentViews.Count).IsEqual(7);
        AssertThat(screen.EquipmentViews[4].Stack).IsEqual(new ItemStack(rig.Type("core:module_o2_tank"), 1));
        AssertThat(screen.EquipmentViews[4].Ref).IsEqual(new SlotRef(_equipmentId, 4));
        AssertThat(screen.EquipmentCaptions[0].Text).IsEqual("Helmet");
        AssertThat(screen.EquipmentCaptions[3].Text).IsEqual("Backpack");
        AssertThat(screen.EquipmentCaptions[4].Text).IsEqual("Module");

        // The module of the oxygen tank raises the capacity of the oxygen to 200; the other rows keep the base values.
        AssertThat(screen.Suit.RowCount).IsEqual(5);
        AssertThat(screen.Suit.RowText(0)).IsEqual("Heating  100 / 100  x1");
        AssertThat(screen.Suit.RowText(4)).IsEqual("Oxygen  75 / 200  x1");
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task GeneratorPanel_ShowsRemainingTime()
    {
        TranslationServer.SetLocale("en");
        await using var scene = EngineTestScene.Create();
        var rig = InventoryRig.Create(scene);
        var entities = new ClientBlockEntities();
        ulong serverTick = 1_100;
        rig.Node.ConfigureStations(entities, (out ulong tick) =>
        {
            tick = serverTick;
            return true;
        });
        rig.Node.GeneratorTime = new ClientGeneratorTime(rig.Node.Inventory, entities, rig.Content.Hazards, 60);
        await scene.WaitFrames(2);

        entities.Apply(new BlockEntityStates(_construct, [new BlockEntityEntry(_cell, new OxygenGeneratorBlockState(_generatorInput, new Tick(1_000)))]));
        rig.Node.Chests.Open(_generatorInput, _construct, _cell);
        rig.Node.Inventory.OnMessage(
            new ContainerState(
                _generatorInput, RejectReason.None, ContainerKind.StationInput, ContainerAccess.Shared, 2, 1,
                [new SlotContent(0, new ItemStack(rig.Type("core:fuel_cell"), 1))]));
        await scene.WaitFrames(3);

        var screen = rig.Node.GetNode<InventoryScreen>("InventoryScreen");
        AssertThat(rig.Node.IsOpen).IsTrue();
        AssertThat(screen.Chest!.Visible).IsTrue();
        AssertThat(screen.Chest.IsGenerator).IsTrue();
        AssertThat(screen.Chest.TitleText).IsEqual("Oxygen generator");
        AssertThat(screen.Chest.AccessSwitchVisible).IsFalse();

        // 100 ticks into the burning cell of 300 s (18000 ticks), one more cell queued: 17900 + 18000 = 35900 ticks = 598.3 s, rounded up to 599 s = 09:59.
        AssertThat(screen.Chest.TimeText).IsEqual("Runs for 09:59");
    }
}
