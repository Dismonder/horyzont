using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Tools;

public sealed class ToolControllerTests
{
    private static readonly ToolInputFrame _idle = Input();

    private sealed class FakeSender : IIntentSender
    {
        private uint _seq = 100;

        public bool CanSend { get; set; } = true;

        public List<Intent> Intents { get; } = [];

        public uint Submit(Intent intent)
        {
            Intents.Add(intent);
            return ++_seq;
        }
    }

    private sealed class Rig
    {
        public Rig(float surfaceRadius = 4000f)
        {
            World = new FakeToolWorldView(surfaceRadius);
            Player = World.Join();
            Feet = World.State.Players.TryGet(Player, out var player) ? player.Position : default;
            Controller = new ToolController(
                World.Rules, World, Sender, Tracker,
                new BlockPalette([new RegistryEntry(World.Stone, WorldFactoryStone), new RegistryEntry(9, "core:glass"), new RegistryEntry(10, BlockTypeRegistry.MissingKey)]),
                new MaterialPalette([new RegistryEntry(1, "core:stone"), new RegistryEntry(2, "core:dirt"), new RegistryEntry(3, "core:bedrock")]),
                NullLogger<ToolController>.Instance);
        }

        public FakeToolWorldView World { get; }

        public PlayerId Player { get; }

        public Vector3 Feet { get; }

        public FakeSender Sender { get; } = new();

        public PendingIntentTracker Tracker { get; } = new(NoEditPrediction.Instance, NullLogger<PendingIntentTracker>.Instance);

        public ToolController Controller { get; }

        public AimRay AimAt(Vector3 target)
        {
            var eye = AimRayBuilder.EyeFromFeet(Feet, World.Rules);
            Assert.True(AimRayBuilder.TryBuild(FakeToolWorldView.Planet, Feet, eye, target - eye, -Vector3.UnitZ, World, World.Rules, out var ray));
            return ray;
        }
    }

    private const string WorldFactoryStone = "core:stone_block";

    private static ToolInputFrame Input(
        bool primaryPressed = false, bool primaryHeld = false, bool secondaryPressed = false, bool secondaryHeld = false, ToolKind? select = null,
        int radius = 0, int palette = 0, int material = 0, int yaw = 0, int up = 0) =>
        new(primaryPressed, primaryHeld, secondaryPressed, secondaryHeld, select, radius, palette, material, yaw, up);

    [Fact]
    public void ToolController_Dig_SendsRemoveIntentFromTheEyeWithSettingsRadius()
    {
        var rig = new Rig();
        var aim = rig.AimAt(rig.World.Ground(2f));
        rig.Controller.Update(Input(radius: 2), aim, 0);

        rig.Controller.Update(Input(primaryPressed: true, primaryHeld: true), aim, 1);

        var intent = Assert.IsType<TerrainToolIntent>(Assert.Single(rig.Sender.Intents));
        Assert.Equal(TerrainEditMode.Remove, intent.Mode);
        Assert.Equal(0, intent.MaterialId);
        Assert.Equal(1.5f, intent.Radius);
        Assert.Equal(aim.Eye, intent.RayOrigin);
        Assert.Equal(aim.Direction, intent.RayDirection);
        Assert.Equal(1, rig.Tracker.Count);
        Assert.Equal(ToolPreviewKind.Sphere, rig.Controller.Preview.Kind);
        Assert.Equal(RejectReason.None, rig.Controller.Preview.Hint);
    }

    [Fact]
    public void ToolController_Fill_SendsAddIntentWithTheSelectedMaterial()
    {
        var rig = new Rig();
        var aim = rig.AimAt(rig.World.Ground(4f));

        rig.Controller.Update(Input(material: 1), aim, 0);
        rig.Controller.Update(Input(secondaryPressed: true, secondaryHeld: true), aim, 1);

        var intent = Assert.IsType<TerrainToolIntent>(Assert.Single(rig.Sender.Intents));
        Assert.Equal(TerrainEditMode.Add, intent.Mode);
        Assert.Equal(2, intent.MaterialId);
        Assert.Equal(rig.Controller.Settings.FillMaterial, intent.MaterialId);
    }

    [Fact]
    public void ToolController_SettingSteps_ChangeToolRadiusPaletteAndOrientation()
    {
        var rig = new Rig();
        var aim = rig.AimAt(rig.World.Ground(4f));

        rig.Controller.Update(Input(select: ToolKind.Builder, palette: 1, yaw: 1, up: 2, material: 5), aim, 0);

        var settings = rig.Controller.Settings;
        Assert.Equal(ToolKind.Builder, settings.Tool);
        Assert.Equal<ushort>(9, settings.BlockType);
        Assert.Equal(BlockOrientation.WithYaw(BlockOrientation.WithUp(BlockOrientation.Identity, 2), 1), settings.Orientation);
        Assert.Equal(1f, settings.Radius);
        Assert.Equal(2, settings.FillMaterial); // 5 steps over the two usable materials: 1 -> 2
        rig.Controller.Update(Input(select: ToolKind.TerrainManipulator, radius: -100), aim, 1);
        Assert.Equal(0.5f, rig.Controller.Settings.Radius);
        Assert.Equal(ToolKind.TerrainManipulator, rig.Controller.Settings.Tool);
    }

    [Fact]
    public void ToolController_SenderNotInGame_SendsNothing()
    {
        var rig = new Rig();
        rig.Sender.CanSend = false;
        var aim = rig.AimAt(rig.World.Ground(2f));

        rig.Controller.Update(Input(primaryPressed: true), aim, 0);
        rig.Controller.Update(Input(secondaryPressed: true), aim, 1);
        rig.Controller.Update(Input(select: ToolKind.Builder, secondaryPressed: true), aim, 2);

        Assert.Empty(rig.Sender.Intents);
        Assert.Equal(0, rig.Tracker.Count);
    }

    [Fact]
    public void ToolController_PendingFull_RefusesLocally()
    {
        var rig = new Rig();
        var aim = rig.AimAt(rig.World.Ground(2f));
        for (uint seq = 1; seq <= PendingIntentTracker.MaxPending; seq++)
        {
            rig.Tracker.Add(seq, new RemoveBlockIntent(new EntityId(1), default), default, 0);
        }

        rig.Controller.Update(Input(primaryPressed: true), aim, 1);

        Assert.Empty(rig.Sender.Intents);
        Assert.True(rig.Controller.WaitingForServer);
        Assert.True(rig.Tracker.IsFull);
        rig.Controller.Update(_idle, aim, 2);
        Assert.False(rig.Controller.WaitingForServer);
    }

    [Fact]
    public void ToolController_HeldDig_RepeatsAtThePacerInterval()
    {
        var rig = new Rig();
        var aim = rig.AimAt(rig.World.Ground(2f));
        for (var tick = 0; tick < 40; tick++)
        {
            rig.Controller.Update(Input(primaryPressed: tick == 0, primaryHeld: true), aim, tick);
        }

        Assert.Equal(4, rig.Sender.Intents.Count); // ticks 0, 13, 26, 39
    }

    [Fact]
    public void ToolController_BuilderOnTerrain_SendsPlaceWithTerrainTargetAndNoRemove()
    {
        var rig = new Rig();
        var aim = rig.AimAt(rig.World.Ground(3f));
        rig.Controller.Update(Input(select: ToolKind.Builder), aim, 0);

        rig.Controller.Update(Input(primaryPressed: true), aim, 1);
        Assert.Empty(rig.Sender.Intents);
        rig.Controller.Update(Input(secondaryPressed: true), aim, 2);

        var place = Assert.IsType<PlaceBlockIntent>(Assert.Single(rig.Sender.Intents));
        var target = Assert.IsType<TerrainTarget>(place.Target);
        Assert.Equal(aim.Eye, target.RayOrigin);
        Assert.Equal(aim.Direction, target.RayDirection);
        Assert.Equal(aim.LookForward, target.LookForward);
        Assert.Equal(rig.World.Stone, place.BlockTypeId);
        Assert.Equal(ToolPreviewKind.NewConstructCell, rig.Controller.Preview.Kind);
    }

    [Fact]
    public void ToolController_BuilderOnConstruct_PlacesOnTheHitFaceAndRemovesTheHitCell()
    {
        var rig = new Rig();
        var eye = AimRayBuilder.EyeFromFeet(rig.Feet, rig.World.Rules);
        var construct = rig.World.AddConstruct(new Vector3(-0.5f, eye.Y - 0.5f, -4.5f));
        var aim = rig.AimAt(new Vector3(0f, eye.Y, -3.5f));
        rig.Controller.Update(Input(select: ToolKind.Builder), aim, 0);

        rig.Controller.Update(Input(secondaryPressed: true), aim, 1);
        rig.Controller.Update(Input(primaryPressed: true), aim, 2);

        var place = Assert.IsType<PlaceBlockIntent>(rig.Sender.Intents[0]);
        var face = Assert.IsType<ConstructFaceTarget>(place.Target);
        Assert.Equal(construct, face.Construct);
        Assert.Equal(new CellPos(0, 0, 0), face.Cell);
        Assert.Equal(Face.PosZ, face.Face);
        var remove = Assert.IsType<RemoveBlockIntent>(rig.Sender.Intents[1]);
        Assert.Equal(construct, remove.Construct);
        Assert.Equal(new CellPos(0, 0, 0), remove.Cell);
    }

    [Fact]
    public void ToolController_UnusableAim_SendsNothingAndClearsThePreview()
    {
        var rig = new Rig();
        var aim = rig.AimAt(rig.World.Ground(2f));
        rig.Controller.Update(Input(primaryPressed: true), aim, 0);
        var sent = rig.Sender.Intents.Count;

        rig.Controller.Update(Input(primaryPressed: true), aim with { Direction = new Vector3(float.NaN, 0f, 0f) }, 1);
        rig.Controller.Update(Input(primaryPressed: true), aim with { LookForward = Vector3.Zero }, 2);

        Assert.Equal(sent, rig.Sender.Intents.Count);
        Assert.Equal(ToolPreviewKind.None, rig.Controller.Preview.Kind);
        rig.Controller.ClearAim(3);
        Assert.Equal(ToolPreviewKind.None, rig.Controller.Preview.Kind);
    }

    [Fact]
    public void ToolController_Update_SteadyState_DoesNotAllocate()
    {
        var rig = new Rig();
        var eye = AimRayBuilder.EyeFromFeet(rig.Feet, rig.World.Rules);
        rig.World.AddConstruct(new Vector3(-0.5f, eye.Y - 0.5f, -4.5f));
        rig.World.OtherFeet.Add(rig.World.Ground(5f));
        var terrainAim = rig.AimAt(rig.World.Ground(2f));
        var constructAim = rig.AimAt(new Vector3(0f, eye.Y, -3.5f));

        // Steady state is reached when a round allocates nothing: in a busy run the JIT promotes the methods during the first rounds.
        var allocated = long.MaxValue;
        for (var round = 0; round < 60 && allocated != 0; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var tick = 0; tick < 1000; tick++)
            {
                rig.Controller.Update(_idle, tick % 2 == 0 ? terrainAim : constructAim, tick);
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            rig.Controller.Update(Input(select: round % 2 == 0 ? ToolKind.Builder : ToolKind.TerrainManipulator), terrainAim, 0);
        }

        Assert.Equal(0, allocated);
        Assert.Empty(rig.Sender.Intents);
    }
}
