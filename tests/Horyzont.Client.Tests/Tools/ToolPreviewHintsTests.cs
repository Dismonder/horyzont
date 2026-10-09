using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Tests.Tools;

/// <summary>
/// The hint of a preview must agree with the reason the real validator gives for the same intent: every scenario runs both.
/// </summary>
public sealed class ToolPreviewHintsTests
{
    private static ToolSettings Settings(FakeToolWorldView world, float radius = 1f, ToolKind tool = ToolKind.TerrainManipulator) =>
        new(tool, radius, FillMaterial: 1, world.Stone, BlockOrientation.Identity);

    private static (FakeToolWorldView World, PlayerId Player, AimRay Aim) Arrange(Func<FakeToolWorldView, Vector3> target, float surfaceRadius = 4000f)
    {
        var world = new FakeToolWorldView(surfaceRadius);
        var player = world.Join();
        world.State.Players.TryGet(player, out var state);
        var feet = state!.Position;
        var eye = AimRayBuilder.EyeFromFeet(feet, world.Rules);
        Assert.True(AimRayBuilder.TryBuild(
            FakeToolWorldView.Planet, feet, eye, target(world) - eye, -Vector3.UnitZ, world, world.Rules, out var aim));
        return (world, player, aim);
    }

    private static TerrainToolIntent Dig(AimRay aim, float radius, bool fill) =>
        new(aim.Planet, aim.Eye, aim.Direction, radius, fill ? TerrainEditMode.Add : TerrainEditMode.Remove, fill ? (byte)1 : (byte)0);

    private static PlaceBlockIntent PlaceOnTerrain(FakeToolWorldView world, AimRay aim) =>
        new(new TerrainTarget(aim.Planet, aim.Eye, aim.Direction, aim.LookForward), world.Stone, 0);

    [Fact]
    public void ToolPreview_Hints_MatchValidatorReasons_ValidDig()
    {
        var (world, player, aim) = Arrange(w => w.Ground(2f));

        var preview = new ToolPreviewBuilder(world.Rules, world).ForTerrainTool(aim, Settings(world), fill: false);

        Assert.Equal(ToolPreviewKind.Sphere, preview.Kind);
        Assert.Equal(RejectReason.None, preview.Hint);
        Assert.Equal(RejectReason.None, world.Sim.Submit(player, Dig(aim, 1f, fill: false)).Reason);
    }

    [Fact]
    public void ToolPreview_Hints_MatchValidatorReasons_BelowCore()
    {
        var (world, player, aim) = Arrange(w => w.Ground(2f), surfaceRadius: 3500f);

        var preview = new ToolPreviewBuilder(world.Rules, world).ForTerrainTool(aim, Settings(world, 0.5f), fill: false);

        Assert.Equal(RejectReason.BelowCore, preview.Hint);
        Assert.Equal(RejectReason.BelowCore, world.Sim.Submit(player, Dig(aim, 0.5f, fill: false)).Reason);
    }

    [Fact]
    public void ToolPreview_Hints_MatchValidatorReasons_FillOnOneself()
    {
        var (world, player, aim) = Arrange(w => w.Ground(1f));
        var builder = new ToolPreviewBuilder(world.Rules, world);

        var fill = builder.ForTerrainTool(aim, Settings(world, 3f), fill: true);
        var dig = builder.ForTerrainTool(aim, Settings(world, 3f), fill: false);

        Assert.Equal(RejectReason.IntersectsPlayer, fill.Hint);
        Assert.Equal(RejectReason.None, dig.Hint);
        Assert.Equal(RejectReason.IntersectsPlayer, world.Sim.Submit(player, Dig(aim, 3f, fill: true)).Reason);
    }

    [Fact]
    public void ToolPreview_Hints_MatchValidatorReasons_FillOnAnotherPlayer()
    {
        var (world, player, aim) = Arrange(w => w.Ground(3f));
        var other = world.Join(identity: 2);
        var otherFeet = world.Ground(3f) + new Vector3(0f, 0.1f, 0f);
        world.MovePlayer(other, otherFeet);
        world.OtherFeet.Add(otherFeet);

        var preview = new ToolPreviewBuilder(world.Rules, world).ForTerrainTool(aim, Settings(world), fill: true);

        Assert.Equal(RejectReason.IntersectsPlayer, preview.Hint);
        Assert.Equal(RejectReason.IntersectsPlayer, world.Sim.Submit(player, Dig(aim, 1f, fill: true)).Reason);
    }

    [Fact]
    public void ToolPreview_Hints_MatchValidatorReasons_ConstructInFrontOfTerrain()
    {
        var (world, player, aim) = Arrange(w => w.Ground(3f));
        var eye = aim.Eye;
        world.AddConstruct(new Vector3(0.4f, eye.Y - 1.6f, -0.5f));

        var preview = new ToolPreviewBuilder(world.Rules, world).ForTerrainTool(aim, Settings(world), fill: false);

        Assert.Equal(RejectReason.NoLineOfSight, preview.Hint);
        Assert.Equal(RejectReason.NoLineOfSight, world.Sim.Submit(player, Dig(aim, 1f, fill: false)).Reason);
    }

    [Fact]
    public void ToolPreview_Hints_MatchValidatorReasons_GhostInTheCapsule()
    {
        var (world, player, aim) = Arrange(w => w.Ground(0.3f));
        Assert.True(ToolTargeting.TryTarget(aim, world, world.Rules, out var target));

        var preview = new ToolPreviewBuilder(world.Rules, world).ForBuilder(aim, Settings(world, tool: ToolKind.Builder), target);

        Assert.Equal(ToolPreviewKind.NewConstructCell, preview.Kind);
        Assert.Equal(RejectReason.IntersectsPlayer, preview.Hint);
        Assert.Equal(RejectReason.IntersectsPlayer, world.Sim.Submit(player, PlaceOnTerrain(world, aim)).Reason);
    }

    [Fact]
    public void ToolPreview_Hints_MatchValidatorReasons_GhostPoseEqualsTheConstructTheServerCreates()
    {
        var (world, player, aim) = Arrange(w => w.Ground(3f));
        Assert.True(ToolTargeting.TryTarget(aim, world, world.Rules, out var target));

        var preview = new ToolPreviewBuilder(world.Rules, world).ForBuilder(aim, Settings(world, tool: ToolKind.Builder), target);
        var result = world.Sim.Submit(player, PlaceOnTerrain(world, aim));

        Assert.Equal(RejectReason.None, preview.Hint);
        Assert.True(result.IsAccepted);
        var create = Assert.IsType<ConstructCreateOp>(result.Applied!.Operation);
        Assert.Equal(create.Origin, preview.Origin);
        Assert.Equal(create.Rotation, preview.Rotation);
    }

    [Fact]
    public void ToolPreview_Hints_MatchValidatorReasons_OccupiedCell()
    {
        var (world, player, aim) = Arrange(w => w.Ground(2f));
        var construct = world.AddConstruct(new Vector3(1.5f, aim.Eye.Y - 1.7f, -0.5f), null, new CellPos(0, 1, 0));
        world.ConstructOverride = new ConstructHit(construct, new CellPos(0, 0, 0), Face.PosY, new Vector3(2f, aim.Eye.Y - 0.7f, 0f), 2f);
        Assert.True(ToolTargeting.TryTarget(aim, world, world.Rules, out var target));

        var preview = new ToolPreviewBuilder(world.Rules, world).ForBuilder(aim, Settings(world, tool: ToolKind.Builder), target);
        var server = world.Sim.Submit(player, new PlaceBlockIntent(new ConstructFaceTarget(construct, new CellPos(0, 0, 0), Face.PosY), world.Stone, 0));

        Assert.Equal(ToolPreviewKind.ConstructCell, preview.Kind);
        Assert.Equal(new CellPos(0, 1, 0), preview.Cell);
        Assert.Equal(RejectReason.CellOccupied, preview.Hint);
        Assert.Equal(RejectReason.CellOccupied, server.Reason);
    }

    [Fact]
    public void ToolPreview_Hints_MatchValidatorReasons_OverlapWithAnotherConstruct()
    {
        var (world, player, aim) = Arrange(w => w.Ground(2f));
        var a = world.AddConstruct(new Vector3(2f, 4002f, 0f));
        world.AddConstruct(new Vector3(2.75f, 4003f, 0f));
        world.ConstructOverride = new ConstructHit(a, new CellPos(0, 0, 0), Face.PosY, new Vector3(2.5f, 4003f, 0.5f), 2f);
        Assert.True(ToolTargeting.TryTarget(aim, world, world.Rules, out var target));

        var preview = new ToolPreviewBuilder(world.Rules, world).ForBuilder(aim, Settings(world, tool: ToolKind.Builder), target);
        var server = world.Sim.Submit(player, new PlaceBlockIntent(new ConstructFaceTarget(a, new CellPos(0, 0, 0), Face.PosY), world.Stone, 0));

        Assert.Equal(RejectReason.CellOccupied, preview.Hint);
        Assert.Equal(RejectReason.CellOccupied, server.Reason);
    }

    [Fact]
    public void ToolPreview_NothingWithinReach_IsEmptyWithNoHitHint()
    {
        var world = new FakeToolWorldView();
        var feet = new Vector3(0f, 4000.1f, 0f);
        var aim = new AimRay(FakeToolWorldView.Planet, AimRayBuilder.EyeFromFeet(feet, world.Rules), Vector3.UnitY, -Vector3.UnitZ, feet);
        var builder = new ToolPreviewBuilder(world.Rules, world);

        var terrain = builder.ForTerrainTool(aim, Settings(world), fill: false);
        var blocks = builder.ForBuilder(aim, Settings(world, tool: ToolKind.Builder), default);

        Assert.Equal(ToolPreviewKind.None, terrain.Kind);
        Assert.Equal(RejectReason.NoHit, terrain.Hint);
        Assert.Equal(ToolPreviewKind.None, blocks.Kind);
        Assert.Equal(RejectReason.NoHit, blocks.Hint);
    }
}
