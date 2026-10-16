using Horyzont.Core.Hashing;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Props;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>
/// Tool tiers when digging terrain (K6-31 D9, D21). The player's tier is the highest tier of the tools in the own hotbar. World materials:
/// 1 stone and 2 dirt (yield tier 1), 3 bedrock (yield tier 2), 4 ash (no yield entry). Tests that refuse a dig first make the valid twin (a
/// player with the right tool), because a refusal alone says nothing about the tier check.
/// </summary>
public sealed class DigToolTierTests
{
    private const byte Stone = 1;
    private const byte Dirt = 2;
    private const byte Bedrock = 3;
    private const byte Ash = 4;

    private static PropWorldFixture Player(bool tool = true, Func<PropWorldFixture, MeasuredSphere?>? measured = null)
    {
        var f = new PropWorldFixture(toolKey: tool ? SurvivalContentFactory.ToolKey : null);
        f.Terrain.MeasureResult = measured?.Invoke(f) ?? MeasuredSphere.SolidOf(Stone, 3);
        return f;
    }

    private static void AssertRefusedUnchanged(PropWorldFixture f, IntentResult result)
    {
        Assert.Equal(PropProtocol.ToolTierTooLow, result.Reason);
        Assert.Null(result.Applied);
        Assert.Empty(f.Terrain.AppliedEdits);
    }

    [Fact]
    public void Dig_NoToolInHotbar_ToolTierTooLow()
    {
        var twin = Player();
        Assert.True(twin.DigGround().IsAccepted);

        var f = Player(tool: false);
        var hash = StateHasher.Hash(f.State);
        var journaled = f.Sim.Journal.Entries.Count;

        var result = f.DigGround();

        // The bare hand is tier 0 and stone needs tier 1; the terrain and the world do not change.
        AssertRefusedUnchanged(f, result);
        Assert.Equal(hash, StateHasher.Hash(f.State));
        Assert.Equal(journaled, f.Sim.Journal.Entries.Count);
        Assert.Equal(0, f.Count(SurvivalContentFactory.StoneKey));
    }

    [Fact]
    public void Dig_MultitoolTier1_Yields()
    {
        var f = Player();

        var result = f.DigGround();

        Assert.True(result.IsAccepted, $"{result.Reason}");
        var compound = Assert.IsType<CompoundOp>(result.Applied!.Operation);
        Assert.Equal(2, compound.Components.Count);
        Assert.Equal(new ItemCreateOp(
            new SlotRef(PropWorldFixture.Bag(), 0), f.Item(SurvivalContentFactory.StoneKey), 6, 500, ItemSource.Harvest), compound.Components[0]);
        Assert.IsType<TerrainEditOp>(compound.Components[1]);
        Assert.Equal(6, f.Count(SurvivalContentFactory.StoneKey));
    }

    [Fact]
    public void Dig_MaterialTier2_Tier1Tool_ToolTierTooLow_TerrainUnchanged()
    {
        // The twin: the tier 2 tool in the hotbar next to the tier 1 one opens bedrock.
        var twin = Player(measured: _ => MeasuredSphere.SolidOf(Bedrock, 2));
        twin.GiveHotbar(1, 2, SurvivalContentFactory.Tool2Key, 1, 1);
        var opened = twin.DigGround();
        Assert.True(opened.IsAccepted, $"{opened.Reason}");
        Assert.Equal(2, twin.Count(SurvivalContentFactory.CrystalKey));

        var f = Player(measured: _ => MeasuredSphere.SolidOf(Bedrock, 2));
        var hash = StateHasher.Hash(f.State);

        AssertRefusedUnchanged(f, f.DigGround());
        Assert.Equal(hash, StateHasher.Hash(f.State));

        // The tier 2 tool in the bag does not count: only the tools in the own hotbar do.
        f.GiveBag(1, 0, SurvivalContentFactory.Tool2Key, 1, 1);
        AssertRefusedUnchanged(f, f.DigGround());
        Assert.Equal(0, f.Count(SurvivalContentFactory.CrystalKey));
    }

    [Fact]
    public void Dig_MaterialWithoutYieldEntry_NoTierCheck()
    {
        var stoneWithoutTool = Player(tool: false);
        AssertRefusedUnchanged(stoneWithoutTool, stoneWithoutTool.DigGround());

        // Ash has no yield entry: it has no requirement, as before, and the bare hand digs it.
        var f = Player(tool: false, measured: _ => MeasuredSphere.SolidOf(Ash, 3));

        var result = f.DigGround();

        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.IsType<TerrainEditOp>(result.Applied!.Operation);
        Assert.Single(f.Terrain.AppliedEdits);
    }

    [Fact]
    public void Dig_NotMeasurable_PlainEdit()
    {
        var measurable = Player(tool: false);
        AssertRefusedUnchanged(measurable, measurable.DigGround());

        // Nothing can be measured (as when the area is not loaded): a plain edit as in K6-29W, with no tier check.
        var f = new PropWorldFixture(toolKey: null);
        Assert.Null(f.Terrain.MeasureResult);

        var result = f.DigGround();

        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.IsType<TerrainEditOp>(result.Applied!.Operation);
    }

    [Fact]
    public void Fill_NoTierCheck()
    {
        var digWithoutTool = Player(tool: false);
        AssertRefusedUnchanged(digWithoutTool, digWithoutTool.DigGround());

        // Filling is not mining: the bare hand fills with the soil it carries.
        var f = Player(tool: false, measured: _ => MeasuredSphere.AirOf(1));
        f.GiveBag(1, 0, SurvivalContentFactory.SoilKey, 50);

        var result = f.FillAt(f.Sim.Ground(2), Dirt);

        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.Single(f.Terrain.AppliedEdits);
        Assert.Equal(47, f.Count(SurvivalContentFactory.SoilKey));
    }

    [Fact]
    public void Dig_WithoutSurvivalContent_K2Behaviour()
    {
        var withContent = Player(tool: false);
        AssertRefusedUnchanged(withContent, withContent.DigGround());

        // Without survival content there are no tool tiers and no yield: the dig is the plain edit of K2.
        var f = new PropWorldFixture(toolKey: null, survival: false);
        f.Terrain.MeasureResult = MeasuredSphere.SolidOf(Stone, 3);

        var result = f.DigGround();

        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.IsType<TerrainEditOp>(result.Applied!.Operation);
    }

    [Fact]
    public void Dig_SurvivalContentWithoutProps_NoToolInHotbar_StillDigs()
    {
        // The tier check belongs to the content that has props (D21: tests of K6-29W and K6-30 stay as they are): a dig with survival
        // content but no props file still yields without any tool, as in the tests of MiningYieldTests.
        var withProps = Player(tool: false);
        AssertRefusedUnchanged(withProps, withProps.DigGround());

        var f = new PropWorldFixture(content: SurvivalContentFactory.CreateTest(), toolKey: null);
        f.Terrain.MeasureResult = MeasuredSphere.SolidOf(Stone, 3);

        var result = f.DigGround();

        Assert.True(result.IsAccepted, $"{result.Reason}");
        var compound = Assert.IsType<CompoundOp>(result.Applied!.Operation);
        Assert.IsType<ItemCreateOp>(compound.Components[0]);
        Assert.Equal(6, f.Count(SurvivalContentFactory.StoneKey));
    }
}
