using Horyzont.Client.Props;
using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Props;
using Horyzont.Core.Protocol.Messages;

using static Horyzont.Client.Tests.Props.PropRig;

namespace Horyzont.Client.Tests.Props;

/// <summary>
/// The hand and the multitool hit props (K6-31 D19): the press goes to the prop under the crosshair instead of the terrain, the prompt
/// says what it does, the hits are paced like the server paces them, and the progress is counted from the content.
/// </summary>
public sealed class PropInteractionTests
{
    [Fact]
    public void HandOnRock_PromptAndSendsHit()
    {
        var rig = new PropRig();
        var rock = rig.Add(PropKindV1.Rock, 2.5f);
        var aim = rig.AimAt(rock, 0.3f);

        rig.Controller.Update(Input(select: ToolKind.None), aim, 0);

        Assert.Equal(new PropPrompt(PropPromptKind.BreakRock, PropKindV1.Rock, 0), rig.Interaction.Prompt);
        Assert.Empty(rig.Sender.Intents);

        rig.Controller.Update(Input(pressed: true), aim, 1);

        var hit = Assert.IsType<PropHitIntent>(Assert.Single(rig.Sender.Intents));
        Assert.Equal(FakeWorldPlanet, hit.Planet);
        Assert.Equal(rock.Id, hit.Prop);
        Assert.Equal<ushort>(3, hit.ToolSlot);
        Assert.Equal(1, rig.Tracker.Count);
    }

    [Fact]
    public void MultitoolOnTree_SendsHitNotDig()
    {
        var rig = new PropRig();
        rig.Held.ToolTier = 1;
        var tree = rig.Add(PropKindV1.Pine, 3f);
        var aim = rig.AimAt(tree, 1.2f);
        Assert.Equal(ToolKind.TerrainManipulator, rig.Controller.Settings.Tool);

        rig.Controller.Update(Input(pressed: true), aim, 0);

        Assert.Equal(new PropPrompt(PropPromptKind.CutTree, PropKindV1.Pine, 1), rig.Interaction.Prompt);
        var hit = Assert.IsType<PropHitIntent>(Assert.Single(rig.Sender.Intents));
        Assert.Equal(tree.Id, hit.Prop);
        Assert.DoesNotContain(rig.Sender.Intents, i => i is TerrainToolIntent);

        // Looking at the ground beside the tree digs as before.
        rig.Controller.Update(Input(pressed: true), AimDown(rig), 40);
        Assert.IsType<TerrainToolIntent>(rig.Sender.Intents[^1]);
        Assert.Null(rig.Interaction.Prompt);
        Assert.Equal(2, rig.Sender.Intents.Count);
    }

    [Fact]
    public void BuilderOnTree_NoHit()
    {
        var rig = new PropRig();
        rig.Held.ToolTier = 1;
        var tree = rig.Add(PropKindV1.Pine, 3f);
        var aim = rig.AimAt(tree, 1.2f);

        rig.Controller.Update(Input(select: ToolKind.Builder), aim, 0);
        rig.Controller.Update(Input(pressed: true), aim, 1);

        Assert.Null(rig.Interaction.Prompt);
        Assert.DoesNotContain(rig.Sender.Intents, i => i is PropHitIntent);
    }

    [Fact]
    public void TierTooLow_PromptRequiresTier_SendsNothing()
    {
        var rig = new PropRig();
        var tree = rig.Add(PropKindV1.Pine, 3f);
        var tall = rig.Add(PropKindV1.PineTall, 5f, lateral: 5f);
        var aim = rig.AimAt(tree, 1.2f);

        // Bare hands (tier 0) on a pine that wants tier 1: told so, and nothing goes out, not even a dig under the tree.
        rig.Controller.Update(Input(select: ToolKind.None), aim, 0);
        rig.Controller.Update(Input(pressed: true), aim, 1);
        Assert.Equal(new PropPrompt(PropPromptKind.ToolTierTooLow, PropKindV1.Pine, 1), rig.Interaction.Prompt);
        Assert.Empty(rig.Sender.Intents);

        // The multitool of tier 1 is not enough for a tall pine (tier 2) either, and with the dig tool the terrain stays untouched.
        rig.Held.ToolTier = 1;
        rig.Controller.Update(Input(select: ToolKind.TerrainManipulator, pressed: true), rig.AimAt(tall, 2f), 30);
        Assert.Equal(new PropPrompt(PropPromptKind.ToolTierTooLow, PropKindV1.PineTall, 2), rig.Interaction.Prompt);
        Assert.Empty(rig.Sender.Intents);
    }

    [Fact]
    public void Pacing_Max4PerSecond()
    {
        var rig = new PropRig();
        rig.Held.ToolTier = 1;
        var boulder = rig.Add(PropKindV1.Boulder, 2.5f);
        var aim = rig.AimAt(boulder, 0.7f);

        // The button is held for a second at 60 ticks per second.
        for (var tick = 0; tick < 60; tick++)
        {
            rig.Controller.Update(Input(pressed: tick == 0, held: true), aim, tick);
        }

        Assert.Equal(4, rig.Sender.Intents.Count(i => i is PropHitIntent));
        Assert.Equal(4, rig.Sender.Intents.Count);
    }

    [Fact]
    public void LocalProgress_CountsHits_ResetsOnHarvest()
    {
        var rig = new PropRig();
        rig.Held.ToolTier = 1;
        var first = rig.Add(PropKindV1.Pine, 3f);
        var aim = rig.AimAt(first, 1.2f);
        Assert.Null(rig.Interaction.Progress);

        // Three hit points, damage 1 + (1 - 1) = 1 per hit.
        rig.Controller.Update(Input(pressed: true), aim, 0);
        Assert.Equal(new PropProgress(first.Id, 1, 3), rig.Interaction.Progress);
        rig.Controller.Update(Input(pressed: true), aim, 20);
        Assert.Equal(new PropProgress(first.Id, 2, 3), rig.Interaction.Progress);

        // The server harvested it (the state arrived): the count is over, the next tree starts from zero.
        rig.MarkHarvested(first);
        rig.Controller.Update(Input(), aim, 21);
        Assert.Null(rig.Interaction.Progress);

        var other = rig.Add(PropKindV1.PineTall, 3f);
        rig.Held.ToolTier = 2;
        rig.Controller.Update(Input(pressed: true), rig.AimAt(other, 1.2f), 40);
        Assert.Equal(new PropProgress(other.Id, 1, 4), rig.Interaction.Progress);
    }

    [Fact]
    public void Rejected53_ShowsReason()
    {
        var rig = new PropRig();
        rig.Held.ToolTier = 1;
        var tree = rig.Add(PropKindV1.Pine, 3f);

        // The client believes its tool is good enough; the server does not (another slot, a stale hotbar): the reason reaches the HUD.
        rig.Controller.Update(Input(pressed: true), rig.AimAt(tree, 1.2f), 0);
        Assert.Equal(1, rig.Tracker.Count);
        Assert.True(rig.Tracker.OnReply(new IntentReply(101, RejectReason.ToolTierTooLow, OpId.None), out _));

        Assert.Equal(RejectReason.ToolTierTooLow, rig.Tracker.LastRejection);
        Assert.Equal(0, rig.Tracker.Count);
    }

    private static AimRay AimDown(PropRig rig)
    {
        var eye = Horyzont.Client.Tools.AimRayBuilder.EyeFromFeet(rig.Feet, rig.World.Rules);
        var target = rig.Feet + (rig.Side * 1.5f) - (rig.Up * 0.5f);
        Assert.True(AimRayBuilder.TryBuild(FakeWorldPlanet, rig.Feet, eye, target - eye, -System.Numerics.Vector3.UnitZ, rig.World, rig.World.Rules, out var ray));
        return ray;
    }

    private static PlanetId FakeWorldPlanet => Horyzont.Client.Tests.Tools.FakeToolWorldView.Planet;
}
