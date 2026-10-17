using System.Numerics;

using Horyzont.Client.Inventory;
using Horyzont.Client.Tests.Tools;
using Horyzont.Client.Tools;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol.Messages;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Inventory;

public sealed class HotbarToolTransitionTests
{
    [Fact]
    public void Update_BuilderThenResourceThenTerrain_StopsAndResumesIntentsWithPreservedSettings()
    {
        var fx = new InventoryFixture();
        fx.Inventory.OnMessage(new ItemRegistry(
            [new(1, "core:multitool"), new(2, "core:build_tool"), new(3, "core:stone")]));
        fx.State(InventoryFixture.HotbarId, ContainerKind.Hotbar, 10, 2,
            (0, InventoryFixture.Tool, 1), (1, InventoryFixture.Gem, 1), (2, InventoryFixture.Stone, 30));
        var selection = new HotbarSelection();
        var world = new FakeToolWorldView();
        var player = world.Join();
        Assert.True(world.State.Players.TryGet(player, out var state));
        var feet = state.Position;
        var eye = AimRayBuilder.EyeFromFeet(feet, world.Rules);
        Assert.True(AimRayBuilder.TryBuild(FakeToolWorldView.Planet, feet, eye, world.Ground(2f) - eye,
            -Vector3.UnitZ, world, world.Rules, out var aim));
        var controller = new ToolController(world.Rules, world, fx.Sender,
            new PendingIntentTracker(NoEditPrediction.Instance, NullLogger<PendingIntentTracker>.Instance),
            new BlockPalette([new(world.Stone, "core:stone_block")]),
            new MaterialPalette([new(1, "core:stone")]), NullLogger<ToolController>.Instance);

        selection.SelectKey(2);
        controller.Update(new(false, false, true, true, ItemToolMap.Default.Resolve(fx.Inventory, selection), 1, 0, 0, 1, 0), aim, 0);
        Assert.IsType<PlaceBlockIntent>(Assert.Single(fx.Sender.Intents));
        Assert.Equal(ToolKind.Builder, controller.Settings.Tool);
        var settings = controller.Settings;

        selection.Step(1);
        controller.Update(new(true, true, true, true, ItemToolMap.Default.Resolve(fx.Inventory, selection), 0, 0, 0, 0, 0), aim, 10);
        Assert.Equal(ToolKind.None, controller.Settings.Tool);
        Assert.Equal(ToolPreviewKind.None, controller.Preview.Kind);
        Assert.Single(fx.Sender.Intents);

        // Replacing the selected resource with a tool must work without another selection event.
        fx.Put(InventoryFixture.Hotbar(2), InventoryFixture.Tool, 1);
        controller.Update(new(true, true, false, false, ItemToolMap.Default.Resolve(fx.Inventory, selection), 0, 0, 0, 0, 0), aim, 20);

        Assert.Equal(ToolKind.TerrainManipulator, controller.Settings.Tool);
        Assert.Equal(ToolPreviewKind.Sphere, controller.Preview.Kind);
        Assert.Equal(2, fx.Sender.Intents.Count);
        Assert.IsType<TerrainToolIntent>(fx.Sender.Intents[1]);
        Assert.Equal(settings.Radius, controller.Settings.Radius);
        Assert.Equal(settings.BlockType, controller.Settings.BlockType);
        Assert.Equal(settings.Orientation, controller.Settings.Orientation);
    }
}
