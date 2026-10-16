using System.Numerics;

using Horyzont.Client.Tests.Props;
using Horyzont.Client.Tests.Tools;
using Horyzont.Client.Tools;
using Horyzont.Core.Intents;
using Horyzont.Core.Props;

namespace Horyzont.Client.Tests.Survival;

/// <summary>
/// Eating from the hotbar (K6-33 D21): a fresh press with food in the selected slot builds the intent, a tool, a held button or a prop under the crosshair build
/// nothing, and the rule of the server refuses a repeat of food that is still strong at once with code 49.
/// </summary>
public sealed class FoodUseTests
{
    private readonly FoodClientFixture _fx = new();

    [Fact]
    public void TryBuild_FoodSelected_EatIntentFromHotbar()
    {
        _fx.Selection.SelectSlot(0);

        var built = _fx.Use.TryBuild(true, false, out var intent, out var refusal);

        Assert.True(built);
        Assert.Equal(new EatIntent(FoodClientFixture.HotbarId, 0), intent);
        Assert.Equal(RejectReason.None, refusal);
    }

    [Fact]
    public void TryBuild_OtherSlot_UsesTheSelectedSlot()
    {
        _fx.Selection.SelectSlot(4);
        Assert.False(_fx.Use.TryBuild(true, false, out _, out _));

        _fx.Inventory.TryGetContainer(FoodClientFixture.HotbarId, out var hotbar);
        _fx.Inventory.OnMessage(
            new Horyzont.Core.Protocol.Messages.ContainerDelta(
                FoodClientFixture.HotbarId, hotbar!.Version + 1, [new Horyzont.Core.Protocol.Messages.SlotContent(4, new Horyzont.Core.Items.ItemStack(FoodClientFixture.Jam, 1))]));

        Assert.True(_fx.Use.TryBuild(true, false, out var intent, out _));
        Assert.Equal(new EatIntent(FoodClientFixture.HotbarId, 4), intent);
    }

    [Fact]
    public void TryBuild_ToolSelected_Nothing()
    {
        _fx.Selection.SelectSlot(1);

        Assert.False(_fx.Use.TryBuild(true, false, out var intent, out var refusal));
        Assert.Null(intent);
        Assert.Equal(RejectReason.None, refusal);
    }

    [Fact]
    public void TryBuild_ResourceOrEmptySlot_Nothing()
    {
        _fx.Selection.SelectSlot(2);
        Assert.False(_fx.Use.TryBuild(true, false, out _, out var stoneRefusal));
        Assert.Equal(RejectReason.None, stoneRefusal);

        _fx.Selection.SelectSlot(9);
        Assert.False(_fx.Use.TryBuild(true, false, out _, out var emptyRefusal));
        Assert.Equal(RejectReason.None, emptyRefusal);
    }

    [Fact]
    public void TryBuild_SameFoodActive_LocalReject49()
    {
        _fx.Selection.SelectSlot(0);
        _fx.Eaten((FoodClientFixture.Berries, 9_000));

        var built = _fx.Use.TryBuild(true, false, out var intent, out var refusal);

        Assert.False(built);
        Assert.Null(intent);
        Assert.Equal(RejectReason.FoodAlreadyActive, refusal);
    }

    [Fact]
    public void TryBuild_SameFoodAtTwentyPercent_Allowed()
    {
        _fx.Selection.SelectSlot(0);

        // Eaten at 9 000: 7 200 ticks (20 %) are left at 9 000 + 36 000 - 7 200; one tick earlier more than 20 % remains.
        var eaten = 9_000UL;
        _fx.Eaten((FoodClientFixture.Berries, eaten));
        _fx.Tick = eaten + FoodClientFixture.BerryTicks - 7_201;
        Assert.False(_fx.Use.TryBuild(true, false, out _, out var early));
        Assert.Equal(RejectReason.FoodAlreadyActive, early);

        _fx.Tick = eaten + FoodClientFixture.BerryTicks - 7_200;
        Assert.True(_fx.Use.TryBuild(true, false, out var intent, out var refusal));
        Assert.NotNull(intent);
        Assert.Equal(RejectReason.None, refusal);
    }

    [Fact]
    public void TryBuild_ExpiredFood_Allowed()
    {
        _fx.Selection.SelectSlot(0);
        _fx.Eaten((FoodClientFixture.Berries, 100));
        _fx.Tick = 100 + FoodClientFixture.BerryTicks;

        Assert.True(_fx.Use.TryBuild(true, false, out _, out _));
    }

    [Fact]
    public void TryBuild_ThreeOtherActiveFoods_LocalReject49()
    {
        _fx.Selection.SelectSlot(0);
        _fx.Eaten((FoodClientFixture.Jam, 9_000), (FoodClientFixture.Item("test:broth"), 9_000), (FoodClientFixture.Item("test:bread"), 9_000));

        Assert.False(_fx.Use.TryBuild(true, false, out _, out var refusal));
        Assert.Equal(RejectReason.FoodAlreadyActive, refusal);
    }

    [Fact]
    public void TryBuild_PropTargeted_Nothing()
    {
        _fx.Selection.SelectSlot(0);

        Assert.False(_fx.Use.TryBuild(true, true, out var intent, out var refusal));
        Assert.Null(intent);
        Assert.Equal(RejectReason.None, refusal);
    }

    [Fact]
    public void TryBuild_HeldNotPressed_Nothing()
    {
        _fx.Selection.SelectSlot(0);

        Assert.False(_fx.Use.TryBuild(false, false, out var intent, out var refusal));
        Assert.Null(intent);
        Assert.Equal(RejectReason.None, refusal);
    }

    [Fact]
    public void TryBuild_ServerTickUnknown_LeavesTheCheckToTheServer()
    {
        _fx.Selection.SelectSlot(0);
        _fx.Eaten((FoodClientFixture.Berries, 9_000));
        _fx.TickKnown = false;

        Assert.True(_fx.Use.TryBuild(true, false, out var intent, out var refusal));
        Assert.NotNull(intent);
        Assert.Equal(RejectReason.None, refusal);
    }

    [Fact]
    public void ToolController_PressWithFood_SendsEat_RepeatRefusedLocally()
    {
        var rig = new PropRig();
        var fx = new FoodClientFixture();
        fx.Selection.SelectSlot(0);
        rig.Controller.Food = fx.Use;
        var aim = AimAtGround(rig);

        rig.Controller.Update(PropRig.Input(select: ToolKind.None), aim, 0);
        rig.Controller.Update(PropRig.Input(pressed: true), aim, 1);

        var eat = Assert.IsType<EatIntent>(Assert.Single(rig.Sender.Intents));
        Assert.Equal(new EatIntent(FoodClientFixture.HotbarId, 0), eat);
        Assert.Equal(1, rig.Tracker.Count);

        // A held button does not eat again.
        rig.Controller.Update(PropRig.Input(held: true), aim, 2);
        Assert.Single(rig.Sender.Intents);

        // The server answered: the food is active, so the next press is refused here and shows the refusal.
        fx.Eaten((FoodClientFixture.Berries, fx.Tick));
        rig.Controller.Update(PropRig.Input(pressed: true), aim, 80);
        Assert.Single(rig.Sender.Intents);
        Assert.Equal(RejectReason.FoodAlreadyActive, rig.Tracker.LastRejection);
    }

    [Fact]
    public void ToolController_PropUnderTheCrosshair_HitsInsteadOfEating()
    {
        var rig = new PropRig();
        var fx = new FoodClientFixture();
        fx.Selection.SelectSlot(0);
        rig.Controller.Food = fx.Use;
        var rock = rig.Add(PropKindV1.Rock, 2.5f);
        var aim = rig.AimAt(rock, 0.3f);

        rig.Controller.Update(PropRig.Input(select: ToolKind.None), aim, 0);
        rig.Controller.Update(PropRig.Input(pressed: true), aim, 1);

        Assert.IsType<PropHitIntent>(Assert.Single(rig.Sender.Intents));
        Assert.DoesNotContain(rig.Sender.Intents, i => i is EatIntent);
    }

    private static AimRay AimAtGround(PropRig rig)
    {
        var target = rig.World.Ground(2f);
        var eye = AimRayBuilder.EyeFromFeet(rig.Feet, rig.World.Rules);
        Assert.True(AimRayBuilder.TryBuild(FakeToolWorldView.Planet, rig.Feet, eye, target - eye, -Vector3.UnitZ, rig.World, rig.World.Rules, out var ray));
        return ray;
    }
}
