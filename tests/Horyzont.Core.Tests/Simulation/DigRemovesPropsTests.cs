using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Core.Tests.Props;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>
/// Digging and filling remove the props whose anchor lies in the sphere of the edit plus 0.5 m (K6-31 D12, D21). The props around the target are
/// not under the tests' control, so each test compares the harvest components with what its own <see cref="PropField"/> finds in the sphere of
/// the edit that was applied. The valid twin of a "not removed" test is a dig that does remove the same prop.
/// </summary>
public sealed class DigRemovesPropsTests
{
    private const float Radius = 1f;
    private const float Margin = 0.5f;
    private const float Slack = 0.02f;
    private const byte Dirt = 2;

    private static PlanetId Planet => PropWorldFixture.Planet;

    /// <summary>A fixture with the player 3 m from the prop; the terrain reports only air (no yield), so a dig has no item components.</summary>
    private static PropWorldFixture Beside(PropKindV1 kind, out PropInstance prop, PropWorldFixture? fixture = null)
    {
        var f = fixture ?? new PropWorldFixture();
        prop = f.FindProp(kind);
        f.StandBeside(prop, 3f);
        f.Terrain.MeasureResult = MeasuredSphere.AirOf(5);
        return f;
    }

    private static Operation[] Components(IntentResult result)
    {
        Assert.True(result.IsAccepted, $"{result.Reason}");
        return Assert.IsType<CompoundOp>(result.Applied!.Operation).Components.ToArray();
    }

    /// <summary>The components of a compound, or the operation itself when the dig stayed a plain edit.</summary>
    private static Operation[] Flatten(IntentResult result)
    {
        Assert.True(result.IsAccepted, $"{result.Reason}");
        return result.Applied!.Operation is CompoundOp compound ? compound.Components.ToArray() : [result.Applied.Operation];
    }

    private static TerrainEditOp LastEdit(Operation[] components) => Assert.IsType<TerrainEditOp>(components[^1]);

    /// <summary>The harvests among the components, ahead of the edit, and the checks of D12 against the field around the edit.</summary>
    private static PropHarvestOp[] AssertHarvestsOfEdit(PropWorldFixture f, Operation[] components, params PropId[] alreadyHarvested)
    {
        var edit = LastEdit(components);
        var harvests = components[..^1].Select(c => Assert.IsType<PropHarvestOp>(c)).ToArray();
        var inSphere = new List<PropInstance>();
        f.Field.CollectInSphere(Planet, edit.Center, edit.Radius + Margin, inSphere);

        // Every prop clearly inside is harvested once, in ascending order, with the tick of the dig; nothing clearly outside is.
        var definitely = inSphere.Where(p => Vector3.Distance(p.Anchor, edit.Center) < edit.Radius + Margin - Slack).Select(p => p.Id)
            .Except(alreadyHarvested).ToHashSet();
        Assert.Subset(harvests.Select(h => h.Prop).ToHashSet(), definitely);
        Assert.All(harvests, h =>
        {
            Assert.Equal(Planet, h.Planet);
            Assert.Equal(f.State.CurrentTick, h.Tick);
            Assert.True(f.Field.TryGetProp(Planet, h.Prop, out var prop));
            Assert.True(Vector3.Distance(prop.Anchor, edit.Center) <= edit.Radius + Margin + Slack, $"{h.Prop} is outside the sphere.");
            Assert.DoesNotContain(h.Prop, alreadyHarvested);
        });
        Assert.Equal(harvests.Select(h => h.Prop.Value).Order().ToArray(), harvests.Select(h => h.Prop.Value).ToArray());
        Assert.Equal(harvests.Length, harvests.Select(h => h.Prop).Distinct().Count());
        return harvests;
    }

    [Fact]
    public void Dig_TreeAnchorInSphere_PropHarvestWithoutItems()
    {
        var f = Beside(PropKindV1.Pine, out var tree);

        var result = f.DigAt(tree.Anchor, Radius);

        var components = Components(result);
        Assert.DoesNotContain(components, c => c is ItemCreateOp);
        var harvests = AssertHarvestsOfEdit(f, components);
        Assert.Contains(tree.Id, harvests.Select(h => h.Prop));
        Assert.True(f.State.HarvestedProps.Contains(Planet, tree.Id));
        Assert.Single(f.Terrain.AppliedEdits);
    }

    [Fact]
    public void Dig_WithYield_ItemsThenPropsThenEdit()
    {
        var f = Beside(PropKindV1.Pine, out var tree);
        f.Terrain.MeasureResult = MeasuredSphere.SolidOf(1, 3);

        var components = Components(f.DigAt(tree.Anchor, Radius));

        // The terrain edit stays last (K6-30 D12); the item components come first, then the props, so the props are not reordered by the yield.
        var firstProp = Array.FindIndex(components, c => c is PropHarvestOp);
        Assert.True(firstProp > 0, "A dig with a yield and a prop has item components, then prop components.");
        Assert.All(components[..firstProp], c => Assert.IsType<ItemCreateOp>(c));
        Assert.Contains(tree.Id, AssertHarvestsOfEdit(f, components).Select(h => h.Prop));
    }

    [Fact]
    public void Dig_NotMeasurable_PropsStillRemoved()
    {
        // The twin: the same dig with a measurable sphere removes the tree.
        var twin = Beside(PropKindV1.Pine, out var treeA);
        Assert.Contains(treeA.Id, AssertHarvestsOfEdit(twin, Components(twin.DigAt(treeA.Anchor, Radius))).Select(h => h.Prop));

        // No tool on the hotbar and no measurement: no tier check, no yield, but the props come from the generated surface and are still removed.
        var f = Beside(PropKindV1.Pine, out var tree, new PropWorldFixture(toolKey: null));
        f.Terrain.MeasureResult = null;

        var result = f.DigAt(tree.Anchor, Radius);

        Assert.NotEqual(RejectReason.ToolTierTooLow, result.Reason);
        var components = Components(result);
        Assert.DoesNotContain(components, c => c is ItemCreateOp);
        Assert.Contains(tree.Id, AssertHarvestsOfEdit(f, components).Select(h => h.Prop));
        Assert.True(f.State.HarvestedProps.Contains(Planet, tree.Id));
        Assert.Single(f.Terrain.AppliedEdits);
    }

    [Fact]
    public void Fill_RockAnchorInSphere_PropRemoved()
    {
        var f = Beside(PropKindV1.Rock, out var rock);
        f.Terrain.MeasureResult = MeasuredSphere.AirOf(0);

        var result = f.FillAt(rock.Anchor, Dirt, Radius);

        // A fill buries the prop (nothing to pay for an empty sphere): the harvest goes before the edit and there are no items.
        var components = Components(result);
        Assert.Equal(TerrainEditMode.Add, LastEdit(components).Mode);
        Assert.Contains(rock.Id, AssertHarvestsOfEdit(f, components).Select(h => h.Prop));
        Assert.True(f.State.HarvestedProps.Contains(Planet, rock.Id));
    }

    [Fact]
    public void Dig_AnchorOutsideSphere_Untouched()
    {
        var twin = Beside(PropKindV1.Pine, out var treeA);
        Assert.Contains(treeA.Id, AssertHarvestsOfEdit(twin, Components(twin.DigAt(treeA.Anchor, Radius))).Select(h => h.Prop));

        // The same tree, a dig 4 m to the side (further than radius + margin): the tree stays; props that happen to stand there do not.
        var f = Beside(PropKindV1.Pine, out var tree);
        var target = PropFixtures.PointBeside(tree, 4f, PropWorldFixture.Surface);

        var result = f.DigAt(target, Radius);

        var components = Flatten(result);
        Assert.True(Vector3.Distance(tree.Anchor, LastEdit(components).Center) > Radius + Margin);
        Assert.False(f.State.HarvestedProps.Contains(Planet, tree.Id));
        AssertHarvestsOfEdit(f, components);
    }

    [Fact]
    public void Dig_AlreadyHarvested_NotRepeated()
    {
        var twin = Beside(PropKindV1.Pine, out var treeA);
        Assert.Contains(treeA.Id, AssertHarvestsOfEdit(twin, Components(twin.DigAt(treeA.Anchor, Radius))).Select(h => h.Prop));

        var f = Beside(PropKindV1.Pine, out var tree);
        f.Apply(new PropHarvestOp(Planet, tree.Id, f.State.CurrentTick));

        // The tree was harvested before: the dig neither repeats the harvest (which would fail the whole compound) nor refuses.
        var result = f.DigAt(tree.Anchor, Radius);

        Assert.True(result.IsAccepted, $"{result.Reason}");
        var components = Flatten(result);
        AssertHarvestsOfEdit(f, components, tree.Id);
        Assert.DoesNotContain(components, c => c is PropHarvestOp h && h.Prop == tree.Id);
        Assert.Single(f.Terrain.AppliedEdits);
    }

    [Fact]
    public void Dig_WithoutPropContent_NoPropComponents()
    {
        var twin = Beside(PropKindV1.Pine, out var treeA);
        Assert.Contains(treeA.Id, AssertHarvestsOfEdit(twin, Components(twin.DigAt(treeA.Anchor, Radius))).Select(h => h.Prop));

        // Survival content without props (D21): a dig is the plain edit of K6-29W and harvests nothing.
        var f = Beside(PropKindV1.Pine, out var tree, new PropWorldFixture(content: SurvivalContentFactory.CreateTest()));

        var result = f.DigAt(tree.Anchor, Radius);

        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.IsType<TerrainEditOp>(result.Applied!.Operation);
        Assert.Equal(0, f.State.HarvestedProps.Count);
    }
}
