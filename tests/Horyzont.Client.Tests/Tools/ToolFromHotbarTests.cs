using System.Numerics;

using Horyzont.Client.Inventory;
using Horyzont.Client.Tests.Inventory;
using Horyzont.Client.Tools;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Blocks;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Tools;

public sealed class ToolFromHotbarTests
{
    private const string Multitool = "core:multitool";
    private const string BuildTool = "core:build_tool";
    private const string Stone = "core:stone";

    private sealed class Rig
    {
        public Rig()
        {
            World = new FakeToolWorldView();
            var player = World.Join();
            Feet = World.State.Players.TryGet(player, out var state) ? state.Position : default;
            Controller = new ToolController(
                World.Rules, World, Sender, new PendingIntentTracker(NoEditPrediction.Instance, NullLogger<PendingIntentTracker>.Instance),
                new BlockPalette([new RegistryEntry(World.Stone, "core:stone_block"), new RegistryEntry(9, "core:glass")]),
                new MaterialPalette([new RegistryEntry(1, "core:stone"), new RegistryEntry(2, "core:dirt")]), NullLogger<ToolController>.Instance);
            Inventory = new ClientInventory(Horyzont.Testing.SurvivalContentFactory.CreateTest().Items, _ => { }, NullLogger<ClientInventory>.Instance);
            Inventory.OnMessage(new ItemRegistry([new RegistryEntry(1, Multitool), new RegistryEntry(2, BuildTool), new RegistryEntry(3, Stone)]));
            Inventory.OnMessage(
                new ContainerState(
                    new(2), RejectReason.None, ContainerKind.Hotbar, ContainerAccess.Private, 10, 1,
                    [new(0, new ItemStack(new(1), 1)), new(1, new ItemStack(new(2), 1)), new(2, new ItemStack(new(3), 30))]));
        }

        public FakeToolWorldView World { get; }

        public Vector3 Feet { get; }

        public InventoryFixture.RecordingSender Sender { get; } = new();

        public ToolController Controller { get; }

        public ClientInventory Inventory { get; }

        public HotbarSelection Selection { get; } = new();

        public AimRay Aim()
        {
            var eye = AimRayBuilder.EyeFromFeet(Feet, World.Rules);
            Assert.True(AimRayBuilder.TryBuild(FakeToolWorldView.Planet, Feet, eye, World.Ground(2f) - eye, -Vector3.UnitZ, World, World.Rules, out var ray));
            return ray;
        }

        public void Tick(long tick, bool primary = false)
        {
            var kind = ItemToolMap.Default.Resolve(Inventory, Selection);
            Controller.Update(
                new ToolInputFrame(primary, primary, false, false, kind, 0, 0, 0, 0, 0), Aim(), tick);
        }
    }

    [Fact]
    public void MultitoolSlot_SelectsTerrain()
    {
        var rig = new Rig();
        rig.Selection.SelectKey(2);
        rig.Tick(0);
        Assert.Equal(ToolKind.Builder, rig.Controller.Settings.Tool);

        rig.Selection.SelectKey(1);
        rig.Tick(1, primary: true);

        Assert.Equal(ToolKind.TerrainManipulator, rig.Controller.Settings.Tool);
        Assert.IsType<TerrainToolIntent>(Assert.Single(rig.Sender.Intents));
    }

    [Fact]
    public void BuildToolSlot_SelectsBuilder()
    {
        var rig = new Rig();
        rig.Selection.SelectKey(2);

        rig.Tick(0);

        Assert.Equal(ToolKind.Builder, rig.Controller.Settings.Tool);
    }

    [Fact]
    public void ResourceSlot_SelectsNone_SendsNothing()
    {
        var rig = new Rig();
        rig.Selection.SelectKey(3);

        rig.Tick(0, primary: true);
        rig.Tick(1, primary: true);

        Assert.Equal(ToolKind.None, rig.Controller.Settings.Tool);
        Assert.Equal(ToolPreviewKind.None, rig.Controller.Preview.Kind);
        Assert.Empty(rig.Sender.Intents);
    }

    [Fact]
    public void EmptySlot_SelectsNone_AndSwitchingBackRestoresTheTool()
    {
        var rig = new Rig();
        rig.Selection.SelectKey(7);
        Assert.Equal(ToolKind.None, ItemToolMap.Default.Resolve(rig.Inventory, rig.Selection));

        rig.Selection.SelectKey(1);
        rig.Tick(0);

        Assert.Equal(ToolKind.TerrainManipulator, rig.Controller.Settings.Tool);
    }

    [Fact]
    public void CustomMap_UsesTheKeysOfTheContent()
    {
        var map = new ItemToolMap(new Dictionary<string, ToolKind> { ["test:tool"] = ToolKind.Builder });

        Assert.Equal(ToolKind.Builder, map.ForKey("test:tool"));
        Assert.Equal(ToolKind.None, map.ForKey("test:stone"));
        Assert.Equal(ToolKind.None, map.ForKey(null));
    }
}
