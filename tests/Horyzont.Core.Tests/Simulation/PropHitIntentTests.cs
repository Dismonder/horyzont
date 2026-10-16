using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Core.Tests.Props;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>
/// <see cref="PropHitIntent"/> (intent 27, K6-31 D9-D11, D21, D24). The test content gives the pine 3 hit points and tool tier 1, the rock 1 and
/// tier 0, the tall pine 4 and tier 2, the boulder 30 and tier 1; hotbar slot 0 holds the tier 1 tool, slot 1 three gems (not a tool), slot 2
/// (when given) the tier 2 tool, the rest is empty (the bare hand). Every refusal test first makes the valid twin of the same hit, because an
/// intent the simulation does not know yet is refused today and the refusal alone would prove nothing.
/// </summary>
public sealed class PropHitIntentTests
{
    private const string Wood = SurvivalContentFactory.WoodKey;
    private const ushort Hand = 9;

    private static PropWorldFixture Near(PropKindV1 kind, out PropInstance prop, float meters = 3f, int players = 1)
    {
        var f = new PropWorldFixture(players);
        prop = f.FindProp(kind);
        for (var n = 1; n <= players; n++)
        {
            f.StandBeside(prop, meters, n);
        }

        return f;
    }

    private static void AssertAcceptedWithoutOperation(IntentResult result)
    {
        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.Null(result.Applied);
        Assert.Equal(IntentResult.AcceptedWithoutOperation(), result);
    }

    private static CompoundOp AssertHarvest(PropWorldFixture f, IntentResult result, PropInstance prop)
    {
        Assert.True(result.IsAccepted, $"{result.Reason}");
        var compound = Assert.IsType<CompoundOp>(result.Applied!.Operation);
        Assert.Equal(new PropHarvestOp(PropWorldFixture.Planet, prop.Id, f.State.CurrentTick), compound.Components[0]);
        Assert.True(f.State.HarvestedProps.Contains(PropWorldFixture.Planet, prop.Id));
        return compound;
    }

    [Fact]
    public void Hit_FirstOfThree_AcceptedWithoutOperation()
    {
        var f = Near(PropKindV1.Pine, out var tree);
        var lastOp = f.State.LastOpId;
        var journaled = f.Sim.Journal.Entries.Count;

        AssertAcceptedWithoutOperation(f.Hit(tree.Id));
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));

        // Wear is a state of the game, not of the world: no operation, no journal entry, nothing harvested.
        Assert.Equal(lastOp, f.State.LastOpId);
        Assert.Equal(journaled, f.Sim.Journal.Entries.Count);
        Assert.Equal(0, f.State.HarvestedProps.Count);
        Assert.Equal(0, f.Count(Wood));
    }

    [Fact]
    public void Hit_Last_CompoundHarvestAndItems()
    {
        var f = Near(PropKindV1.Pine, out var tree);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));

        var result = f.Hit(tree.Id);

        var compound = AssertHarvest(f, result, tree);
        var items = compound.Components.Skip(1).Cast<ItemCreateOp>().ToArray();
        Assert.NotEmpty(items);
        Assert.All(items, i =>
        {
            Assert.Equal(f.Item(Wood), i.Type);
            Assert.Equal(ItemSource.Harvest, i.Source);
            Assert.Equal(500, i.StackLimit);
        });
        Assert.Equal(3, items.Sum(i => i.Count));
        Assert.Equal(3, f.Count(Wood));
        Assert.Same(compound, f.Sim.Journal.Entries[^1].Operation);
    }

    [Fact]
    public void Hit_HandOnTier1Tree_ToolTierTooLow()
    {
        var withTool = Near(PropKindV1.Pine, out var treeA);
        AssertAcceptedWithoutOperation(withTool.Hit(treeA.Id, 0));

        var f = Near(PropKindV1.Pine, out var tree);
        var result = f.Hit(tree.Id, Hand);

        Assert.Equal(RejectReason.ToolTierTooLow, result.Reason);
        Assert.Null(result.Applied);
        Assert.Equal(0, f.State.HarvestedProps.Count);
        Assert.Equal(0, f.Count(Wood));
    }

    [Fact]
    public void Hit_HandOnRock_Accepted()
    {
        var f = Near(PropKindV1.Rock, out var rock);

        var result = f.Hit(rock.Id, Hand);

        // The rock needs tier 0 and has 1 hit point: the first bare-handed hit takes it.
        var compound = AssertHarvest(f, result, rock);
        Assert.Equal(2, compound.Components.Skip(1).Cast<ItemCreateOp>().Sum(i => i.Count));
        Assert.Equal(2, f.Count(SurvivalContentFactory.StoneKey));
    }

    [Fact]
    public void Hit_HigherTier_MoreDamage()
    {
        // Tier 1 tool on the tier 1 tree: damage 1, so the second of three hits does not finish it.
        var control = Near(PropKindV1.Pine, out var treeA);
        AssertAcceptedWithoutOperation(control.Hit(treeA.Id, 0));
        AssertAcceptedWithoutOperation(control.Hit(treeA.Id, 0));

        // The tier 2 tool does 1 + (2 - 1) = 2: two hits finish three hit points.
        var f = Near(PropKindV1.Pine, out var tree);
        f.GiveHotbar(1, 2, SurvivalContentFactory.Tool2Key, 1, 1);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id, 2));
        var result = f.Hit(tree.Id, 2);

        AssertHarvest(f, result, tree);
    }

    [Fact]
    public void Hit_EmptySlotIsHand()
    {
        var f = Near(PropKindV1.Pine, out var tree);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id, 0));

        // An empty slot is the bare hand (tier 0), not an invalid slot: the tier 1 tree refuses it with the tier reason.
        Assert.Equal(RejectReason.ToolTierTooLow, f.Hit(tree.Id, 5).Reason);
        Assert.Equal(RejectReason.ToolTierTooLow, f.Hit(tree.Id, Hand).Reason);
    }

    [Fact]
    public void Hit_NonToolItemIsHand()
    {
        var f = Near(PropKindV1.Pine, out var tree);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id, 0));

        // Slot 1 holds gems, which have no tool flag: the hand again.
        Assert.Equal(RejectReason.ToolTierTooLow, f.Hit(tree.Id, 1).Reason);

        var rock = Near(PropKindV1.Rock, out var stone);
        AssertHarvest(rock, rock.Hit(stone.Id, 1), stone);
    }

    [Fact]
    public void Hit_SlotOutOfHotbar_InvalidParameters()
    {
        var f = Near(PropKindV1.Pine, out var tree);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id, 0));

        // Slot 9 is the last of the hotbar (an empty one, the bare hand: the tier reason shows that the slot passed); 10 and above are not.
        Assert.Equal(RejectReason.ToolTierTooLow, f.Hit(tree.Id, 9).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Hit(tree.Id, 10).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Hit(tree.Id, 255).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Hit(tree.Id, ushort.MaxValue).Reason);
    }

    [Fact]
    public void Hit_UnknownProp_InvalidParameters()
    {
        var f = Near(PropKindV1.Pine, out var tree);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));

        // Slots 2 and 3 of the cell are not used by layout V1, and a cell outside the grid of the planet (784 cells a face) does not exist.
        var unusedSlot = new PropId(tree.Id.Value | (2UL << 16));
        var outside = PropId.Create(tree.Id.Face, 5000, 5000, 0);
        Assert.False(f.Field.TryGetProp(PropWorldFixture.Planet, unusedSlot, out _));
        Assert.False(f.Field.TryGetProp(PropWorldFixture.Planet, outside, out _));

        Assert.Equal(RejectReason.InvalidParameters, f.Hit(unusedSlot).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Hit(outside).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Hit(new PropId(tree.Id.Value | 1UL)).Reason);
    }

    [Fact]
    public void Hit_HarvestedProp_NoHit()
    {
        var f = Near(PropKindV1.Pine, out var tree);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));

        f.Apply(new PropHarvestOp(PropWorldFixture.Planet, tree.Id, f.State.CurrentTick));

        // The tree is gone for everyone: a hit that arrives late (two players on one tree) finds nothing, whatever its tool.
        Assert.Equal(RejectReason.NoHit, f.Hit(tree.Id).Reason);
        Assert.Equal(RejectReason.NoHit, f.Hit(tree.Id, Hand).Reason);
    }

    [Fact]
    public void Hit_OtherPlanet_WrongPlanet()
    {
        var f = Near(PropKindV1.Pine, out var tree);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));

        // A second planet the world knows, but the player is not on it: the planet is known, so it is the wrong one and not an unknown one.
        f.State.Planets.Add(new PlanetParams(new PlanetId(2), new SystemId(1), 9, WorldFactory.PropProfile));
        f.Sim.Ticks(15);
        var result = f.Submit(new PropHitIntent(new PlanetId(2), tree.Id, 0));

        Assert.Equal(RejectReason.WrongPlanet, result.Reason);
    }

    [Fact]
    public void Hit_UnknownPlanet_UnknownPlanet()
    {
        var f = Near(PropKindV1.Pine, out var tree);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));

        f.Sim.Ticks(15);
        var result = f.Submit(new PropHitIntent(new PlanetId(99), tree.Id, 0));

        Assert.Equal(RejectReason.UnknownPlanet, result.Reason);
        Assert.Equal(0, f.State.HarvestedProps.Count);
    }

    [Fact]
    public void Hit_Refused_DoesNotWear()
    {
        var f = Near(PropKindV1.Pine, out var tree);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));

        // Refusals between the hits (reach, tool tier, line of sight) leave the hit points as they are.
        Assert.Equal(RejectReason.ToolTierTooLow, f.Hit(tree.Id, Hand).Reason);
        f.StandBeside(tree, 14f);
        Assert.Equal(RejectReason.OutOfReach, f.Hit(tree.Id).Reason);
        f.StandUnderground(tree, 3f);
        Assert.Equal(RejectReason.NoLineOfSight, f.Hit(tree.Id).Reason);
        f.StandBeside(tree, 3f);

        // Three accepted hits finish the tree: the third one does it, so the refusals above did not count as hits.
        Assert.Equal(0, f.State.HarvestedProps.Count);
        AssertHarvest(f, f.Hit(tree.Id), tree);
    }

    [Fact]
    public void Hit_AfterRegrow_FullHitPoints()
    {
        var f = Near(PropKindV1.Pine, out var tree);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));
        AssertHarvest(f, f.Hit(tree.Id), tree);

        // The ledger entry is gone with the harvest: after the regrowth the tree needs its three hits again, not one.
        f.Apply(new PropRegrowOp(PropWorldFixture.Planet, tree.Id));
        Assert.Equal(0, f.State.HarvestedProps.Count);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));
        AssertHarvest(f, f.Hit(tree.Id), tree);
    }

    [Fact]
    public void Hit_Beyond8mFromTrunk_OutOfReach()
    {
        var f = Near(PropKindV1.Pine, out var tree, 3f);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));

        f.StandBeside(tree, 14f);
        var result = f.Hit(tree.Id);

        Assert.Equal(RejectReason.OutOfReach, result.Reason);
        Assert.Equal(0, f.State.HarvestedProps.Count);
    }

    [Fact]
    public void Hit_TrunkTopWithinReach_Accepted()
    {
        var f = Near(PropKindV1.PineTall, out var tree);
        Assert.InRange(tree.TrunkHeightMeters, 4f, 40f);
        f.GiveHotbar(1, 2, SurvivalContentFactory.Tool2Key, 1, 1);

        // Standing 2 m beside the trunk 90 % of its height up (on a tower): the eye is more than 8 m from the anchor but 2 m from the trunk.
        f.StandAbove(tree, 2f, tree.TrunkHeightMeters * 0.9f);
        var eyeToAnchor = f.Sim.Eye(PropWorldFixture.Player()) - tree.Anchor;
        Assert.True(eyeToAnchor.Length() > 8f + tree.BodyRadiusMeters, $"The eye is {eyeToAnchor.Length()} m from the anchor; use a taller trunk.");

        var result = f.Hit(tree.Id, 2);

        AssertAcceptedWithoutOperation(result);

        // The same height far to the side is out of reach.
        f.StandAbove(tree, 14f, tree.TrunkHeightMeters * 0.9f);
        Assert.Equal(RejectReason.OutOfReach, f.Hit(tree.Id, 2).Reason);
    }

    [Fact]
    public void Hit_BehindHill_NoLineOfSight()
    {
        var f = Near(PropKindV1.Pine, out var tree);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));

        // The player is in a pit under the surface: the terrain lies between the eye and the trunk.
        f.StandUnderground(tree, 3f);
        var result = f.Hit(tree.Id);

        Assert.Equal(RejectReason.NoLineOfSight, result.Reason);
        Assert.Equal(0, f.State.HarvestedProps.Count);
    }

    [Fact]
    public void Hit_WithoutPropContent_InvalidParameters()
    {
        var f = Near(PropKindV1.Pine, out var tree);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));

        // Survival content without the props file (D21) and no survival content at all (K2): the intent is not valid.
        foreach (var survival in new[] { true, false })
        {
            var plain = new PropWorldFixture(content: survival ? SurvivalContentFactory.CreateTest() : null, survival: survival);
            plain.StandBeside(tree, 3f);

            Assert.Equal(RejectReason.InvalidParameters, plain.Hit(tree.Id).Reason);
        }
    }

    [Fact]
    public void Hits_After30sPause_HitPointsReset()
    {
        // Without a pause the third hit finishes the tree.
        var control = Near(PropKindV1.Pine, out var treeA);
        AssertAcceptedWithoutOperation(control.Hit(treeA.Id));
        AssertAcceptedWithoutOperation(control.Hit(treeA.Id));
        AssertHarvest(control, control.Hit(treeA.Id), treeA);

        // A pause of 1800 ticks (30 s) restores the hit points, so the third hit only wears the tree again.
        var f = Near(PropKindV1.Pine, out var tree);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));
        f.Sim.Ticks(1800);
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));
        AssertAcceptedWithoutOperation(f.Hit(tree.Id));
        Assert.Equal(0, f.State.HarvestedProps.Count);
        AssertHarvest(f, f.Hit(tree.Id), tree);
    }

    [Fact]
    public void Hit_InventoryFull_HarvestWithoutItems()
    {
        var f = Near(PropKindV1.Rock, out var rock);
        f.FillWithTools(PropWorldFixture.Bag(), 0, WorldFactory.BagSize);
        f.FillWithTools(PropWorldFixture.Hotbar(), 2, WorldFactory.HotbarSize - 2);
        var tools = f.Count(SurvivalContentFactory.ToolKey);

        var result = f.Hit(rock.Id, 0);

        // Nothing fits, so the harvest is the only component (D11): the prop is gone, the items are lost, and the hit is not refused.
        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.Equal(new PropHarvestOp(PropWorldFixture.Planet, rock.Id, f.State.CurrentTick), result.Applied!.Operation);
        Assert.True(f.State.HarvestedProps.Contains(PropWorldFixture.Planet, rock.Id));
        Assert.Equal(0, f.Count(SurvivalContentFactory.StoneKey));
        Assert.Equal(tools, f.Count(SurvivalContentFactory.ToolKey));
    }

    [Fact]
    public void Burst_PropsBucket_RateLimited()
    {
        var f = Near(PropKindV1.Boulder, out var boulder);
        var hit = new PropHitIntent(PropWorldFixture.Planet, boulder.Id, 0);

        // Four hits at once (the burst), the fifth waits for a token; the wearing hits count as well as the last one.
        for (var i = 0; i < 4; i++)
        {
            AssertAcceptedWithoutOperation(f.Submit(hit));
        }

        Assert.Equal(RejectReason.RateLimited, f.Submit(hit).Reason);

        f.Sim.Ticks(15);
        AssertAcceptedWithoutOperation(f.Submit(hit));
        Assert.Equal(RejectReason.RateLimited, f.Submit(hit).Reason);
    }

    [Fact]
    public void TwoPlayersSameTree_SecondFinalHit_NoHit()
    {
        var f = Near(PropKindV1.Pine, out var tree, players: 2);
        // The wear belongs to the tree, not to the player: 1 + 1 + 1 hits of two players finish three hit points.
        AssertAcceptedWithoutOperation(f.Hit(tree.Id, 0, 1));
        AssertAcceptedWithoutOperation(f.Hit(tree.Id, 0, 2));
        var last = f.Hit(tree.Id, 0, 1);
        AssertHarvest(f, last, tree);

        // The second player's next hit arrives after the harvest and finds nothing; nothing is created for them.
        var late = f.Hit(tree.Id, 0, 2);

        Assert.Equal(RejectReason.NoHit, late.Reason);
        Assert.Equal(3, f.Count(Wood, 1));
        Assert.Equal(0, f.Count(Wood, 2));
    }
}
