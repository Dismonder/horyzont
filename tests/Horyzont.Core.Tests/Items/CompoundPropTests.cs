using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Props;

using static Horyzont.Core.Tests.Items.ItemFixture;
using static Horyzont.Core.Tests.Props.PropFixture;

namespace Horyzont.Core.Tests.Items;

/// <summary>K6-31 D13: the components 47 and 48 in a compound operation. Every refusal starts from the valid counterpart.</summary>
public sealed class CompoundPropTests
{
    [Fact]
    public void HarvestWithItems_Compound_AddsPropAndItems()
    {
        var f = new PropFixture();
        Assert.True(f.State.Containers.TryGet(Bag, out var before));
        var version = before.Version;

        var applied = f.Apply(new CompoundOp([Harvest(Tree(0), 77), Create(BagSlot(0), Stone, 4, source: ItemSource.Harvest)]));

        Assert.True(f.State.HarvestedProps.TryGetTick(Planet, Tree(0), out var tick));
        Assert.Equal(new Tick(77), tick);
        Assert.True(f.State.Containers.TryGet(Bag, out var bag));
        Assert.Equal(new ItemStack(Stone, 4), bag.GetSlot(0));
        Assert.Equal(version + 1, bag.Version);
        Assert.Equal(Bag, Assert.Single(applied.ContainerChanges).Container);
        Assert.Empty(applied.TerrainVersions);
        Assert.Empty(applied.ConstructChanges);
    }

    [Fact]
    public void FailingItemComponent_PropNotHarvested()
    {
        var f = new PropFixture();
        f.Apply(new CompoundOp([Harvest(Tree(0)), Create(BagSlot(0), Stone, 4, source: ItemSource.Harvest)]));
        var failing = new CompoundOp([Harvest(Tree(1)), Destroy(BagSlot(0), Stone, 501)]);

        Assert.False(f.TryApplyRaw(failing, out _, out var failure, out var index));

        Assert.Equal(1, index);
        Assert.NotEmpty(failure);
        Assert.False(f.IsHarvested(Tree(1)));
        f.AssertFails(failing);
        Assert.True(f.IsHarvested(Tree(0)));
    }

    [Fact]
    public void DigCompound_PropsBeforeTerrainLast_Applies()
    {
        var f = new PropFixture();
        var dig = Dig(f);

        var applied = f.Apply(new CompoundOp([Create(BagSlot(0), Stone, 2, source: ItemSource.Harvest), Harvest(Tree(0)), Harvest(Rock(0)), dig]));

        Assert.True(f.IsHarvested(Tree(0)));
        Assert.True(f.IsHarvested(Rock(0)));
        Assert.Single(f.Sim.Terrain.AppliedEdits);
        Assert.NotEmpty(applied.TerrainVersions);
        Assert.True(OperationTerrain.TryGetTerrainEdit(applied.Operation, out var edit));
        Assert.Equal(dig, edit);
        var changes = new List<PropChange>();
        OperationProps.CollectChanges(applied.Operation, changes);
        Assert.Equal([new PropChange(Planet, Tree(0), true), new PropChange(Planet, Rock(0), true)], changes);
    }

    [Fact]
    public void PropAfterTerrain_StructureFails()
    {
        var f = new PropFixture();
        f.Apply(new CompoundOp([Harvest(Tree(0)), Dig(f)]));
        var edits = f.Sim.Terrain.AppliedEdits.Count;
        var wrongOrder = new CompoundOp([Dig(f), Harvest(Tree(1))]);

        Assert.False(f.TryApplyRaw(wrongOrder, out _, out _, out var index));

        Assert.Equal(0, index);
        f.AssertFails(wrongOrder);
        Assert.False(f.IsHarvested(Tree(1)));
        Assert.Equal(edits, f.Sim.Terrain.AppliedEdits.Count);
    }

    [Fact]
    public void TwoHarvestsSameProp_SecondComponentFails_RolledBack()
    {
        var f = new PropFixture();
        f.Apply(new CompoundOp([Harvest(Tree(0)), Harvest(Tree(1))]));
        var twice = new CompoundOp([Create(BagSlot(0), Stone, 3, source: ItemSource.Harvest), Harvest(Tree(2)), Harvest(Tree(2))]);

        Assert.False(f.TryApplyRaw(twice, out _, out _, out var index));

        Assert.Equal(2, index);
        Assert.False(f.IsHarvested(Tree(2)));
        f.AssertFails(twice);
        Assert.Equal(2, f.State.HarvestedProps.Count);
        Assert.True(f.State.Containers.TryGet(Bag, out var bag));
        Assert.True(bag.GetSlot(0).IsEmpty);
    }

    [Fact]
    public void RegrowCompound_Of64_AllRemoved()
    {
        var f = new PropFixture();
        var props = Enumerable.Range(0, 64).Select(Tree).ToArray();
        f.Apply(new CompoundOp(props.Select(p => (Operation)Harvest(p)).ToList()));
        Assert.Equal(64, f.State.HarvestedProps.Count);

        var applied = f.Apply(new CompoundOp(props.Select(p => (Operation)Regrow(p)).ToList()));

        Assert.Equal(0, f.State.HarvestedProps.Count);
        var changes = new List<PropChange>();
        OperationProps.CollectChanges(applied.Operation, changes);
        Assert.Equal(64, changes.Count);
        Assert.All(changes, c => Assert.False(c.Harvested));
    }

    [Fact]
    public void FailedCompound_ThenStandalone_NoStaleUndo()
    {
        var f = new PropFixture();
        f.Apply(Harvest(Tree(0)));
        var failing = new CompoundOp([Harvest(Tree(1)), Regrow(Tree(0)), Regrow(Tree(0))]);

        Assert.False(f.TryApplyRaw(failing, out _, out _, out var index));
        Assert.Equal(2, index);

        // The undo log of the failed compound must not leak into the next operation, whether that succeeds or fails.
        f.Apply(Harvest(Tree(2)));
        f.AssertFails(Harvest(Tree(2)));
        Assert.True(f.IsHarvested(Tree(0)));
        Assert.False(f.IsHarvested(Tree(1)));
        Assert.True(f.IsHarvested(Tree(2)));
        var hash = StateHasher.Hash(f.State);
        f.Apply(Regrow(Tree(2)));
        Assert.NotEqual(hash, StateHasher.Hash(f.State));
        Assert.Equal(1, f.State.HarvestedProps.Count);
    }

    private static TerrainEditOp Dig(PropFixture f) =>
        new(SimFixture.Planet, TerrainShape.Sphere, f.Sim.Ground(3), 0.5f, TerrainEditMode.Remove, 0);
}
