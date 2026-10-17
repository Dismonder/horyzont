using System.Numerics;

using Horyzont.Client.Tests.Props;
using Horyzont.Client.Tests.Tools;
using Horyzont.Client.Tools;
using Horyzont.Core.Intents;
using Horyzont.Core.Survival;

namespace Horyzont.Client.Tests.Survival;

/// <summary>
/// Refilling the suit from the hotbar (K6-34 D20): a fresh press with a refill in the selected slot builds the intent; a tool, food, a held button or a prop under the crosshair build nothing;
/// a resource that is full (within a point of its capacity) is not sent but reported, so the HUD can say so.
/// </summary>
public sealed class SuitRefillUseTests
{
    private readonly HazardClientFixture _fx = new();

    [Fact]
    public void TryBuild_RefillSelected_IntentFromHotbar()
    {
        _fx.Selection.SelectSlot(0);
        _fx.Told(new SuitResources(40f, 100f, 100f, 100f, 100f));

        var built = _fx.Use.TryBuild(true, false, out var intent, out var full);

        Assert.True(built);
        Assert.Equal(new SuitRefillIntent(HazardClientFixture.HotbarId, 0), intent);
        Assert.False(full);
    }

    [Fact]
    public void TryBuild_NoMessageYet_Sends()
    {
        _fx.Selection.SelectSlot(0);

        // Before the first message 44 nothing is known, so the server decides.
        Assert.True(_fx.Use.TryBuild(true, false, out var intent, out var full));
        Assert.NotNull(intent);
        Assert.False(full);
    }

    [Fact]
    public void TryBuild_ResourceFull_LocalText()
    {
        _fx.Selection.SelectSlot(0);
        _fx.Told(new SuitResources(99.5f, 100f, 100f, 100f, 100f));

        var built = _fx.Use.TryBuild(true, false, out var intent, out var full);

        // 99.5 of 100 is within a point of the capacity: nothing goes to the server, the caller shows "the suit is full".
        Assert.False(built);
        Assert.Null(intent);
        Assert.True(full);

        // The twin: 98.5 is not full (a point and a half below), the refill is sent.
        _fx.Told(new SuitResources(98.5f, 100f, 100f, 100f, 100f));
        Assert.True(_fx.Use.TryBuild(true, false, out _, out var fullNow));
        Assert.False(fullNow);
    }

    [Fact]
    public void TryBuild_FullIsMeasuredAgainstTheCapacityOfTheMessage()
    {
        _fx.Selection.SelectSlot(0);

        // The capacity of the heating is 150 with a module: 120 is not full although it is above the base 100.
        _fx.Told(new SuitResources(120f, 100f, 100f, 100f, 100f), new SuitResources(150f, 100f, 100f, 100f, 100f));

        Assert.True(_fx.Use.TryBuild(true, false, out _, out var full));
        Assert.False(full);
    }

    [Fact]
    public void TryBuild_FoodOrToolSelected_Nothing()
    {
        _fx.Told(new SuitResources(40f, 100f, 100f, 100f, 100f));

        // Berries (slot 3) are food, slot 1 is a tool, slot 2 is stone: none refills the suit.
        foreach (var slot in new[] { 1, 2, 3 })
        {
            _fx.Selection.SelectSlot(slot);
            Assert.False(_fx.Use.TryBuild(true, false, out var intent, out var full));
            Assert.Null(intent);
            Assert.False(full);
        }
    }

    [Fact]
    public void TryBuild_HeldButtonOrPropTargeted_Nothing()
    {
        _fx.Selection.SelectSlot(0);
        _fx.Told(new SuitResources(40f, 100f, 100f, 100f, 100f));

        Assert.False(_fx.Use.TryBuild(false, false, out _, out _));
        Assert.False(_fx.Use.TryBuild(true, true, out _, out _));
        Assert.True(_fx.Use.TryBuild(true, false, out _, out _));
    }

    [Fact]
    public void ToolController_PressWithRefill_Sends_FullResourceOnlyNotes()
    {
        var rig = new PropRig();
        _fx.Selection.SelectSlot(0);
        rig.Controller.SuitRefill = _fx.Use;
        var aim = AimAtGround(rig);
        _fx.Told(new SuitResources(40f, 100f, 100f, 100f, 100f));

        rig.Controller.Update(PropRig.Input(select: ToolKind.None), aim, 0);
        rig.Controller.Update(PropRig.Input(pressed: true), aim, 1);

        Assert.Equal(new SuitRefillIntent(HazardClientFixture.HotbarId, 0), Assert.IsType<SuitRefillIntent>(Assert.Single(rig.Sender.Intents)));
        Assert.Null(rig.Controller.SuitFullTick);

        _fx.Told(new SuitResources(100f, 100f, 100f, 100f, 100f));
        rig.Controller.Update(PropRig.Input(pressed: true), aim, 80);

        Assert.Single(rig.Sender.Intents);
        Assert.Equal(80, rig.Controller.SuitFullTick);
    }

    private static AimRay AimAtGround(PropRig rig)
    {
        var target = rig.World.Ground(2f);
        var eye = AimRayBuilder.EyeFromFeet(rig.Feet, rig.World.Rules);
        Assert.True(AimRayBuilder.TryBuild(FakeToolWorldView.Planet, rig.Feet, eye, target - eye, -Vector3.UnitZ, rig.World, rig.World.Rules, out var ray));
        return ray;
    }
}
