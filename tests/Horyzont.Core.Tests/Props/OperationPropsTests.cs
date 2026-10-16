using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.Tests.Items;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Props.PropFixture;

namespace Horyzont.Core.Tests.Props;

/// <summary>K6-31 D18: the prop changes of an operation, used by replication, the client views and the registry of changes.</summary>
public sealed class OperationPropsTests
{
    [Fact]
    public void Changes_HarvestCompound_ListsProp()
    {
        var harvest = new CompoundOp([Harvest(Tree(0)), ItemFixture.Create(ItemFixture.BagSlot(0), ItemFixture.Stone, 4, source: ItemSource.Harvest)]);
        var changes = new List<PropChange>();

        OperationProps.CollectChanges(harvest, changes);

        Assert.Equal([new PropChange(Planet, Tree(0), true)], changes);
        changes.Clear();
        OperationProps.CollectChanges(Harvest(Rock(1)), changes);
        OperationProps.CollectChanges(Regrow(Tree(0)), changes);
        Assert.Equal([new PropChange(Planet, Rock(1), true), new PropChange(Planet, Tree(0), false)], changes);
    }

    [Fact]
    public void Changes_DigCompound_ListsProps()
    {
        var dig = new TerrainEditOp(Planet, TerrainShape.Sphere, new System.Numerics.Vector3(0, 4000, 0), 0.5f, TerrainEditMode.Remove, 0);
        var compound = new CompoundOp([ItemFixture.Create(ItemFixture.BagSlot(0), ItemFixture.Stone, 2, source: ItemSource.Harvest), Harvest(Tree(2)), Harvest(Rock(0)), dig]);
        var changes = new List<PropChange>();

        OperationProps.CollectChanges(compound, changes);

        Assert.Equal([new PropChange(Planet, Tree(2), true), new PropChange(Planet, Rock(0), true)], changes);
    }

    [Fact]
    public void Changes_ItemOnly_Empty()
    {
        var changes = new List<PropChange>();

        OperationProps.CollectChanges(new CompoundOp([ItemFixture.Create(ItemFixture.BagSlot(0), ItemFixture.Stone, 2)]), changes);
        OperationProps.CollectChanges(ItemFixture.Create(ItemFixture.BagSlot(0), ItemFixture.Stone, 2), changes);
        OperationProps.CollectChanges(new CompoundOp([]), changes);

        Assert.Empty(changes);
    }

    [Fact]
    public void ConstructPart_PropCompound_False()
    {
        var harvest = new CompoundOp([Harvest(Tree(0)), ItemFixture.Create(ItemFixture.BagSlot(0), ItemFixture.Stone, 4, source: ItemSource.Harvest)]);
        var regrow = new CompoundOp([Regrow(Tree(0)), Regrow(Tree(1))]);

        Assert.False(OperationConstructs.TryGetConstructPart(harvest, out var part));
        Assert.Null(part);
        Assert.False(OperationConstructs.TryGetConstructPart(regrow, out _));
        Assert.False(OperationConstructs.TryGetConstructPart(Harvest(Tree(0)), out _));
        Assert.False(OperationConstructs.TryGetConstructPart(Regrow(Tree(0)), out _));

        // A function state component next to a prop component is still found, and the prop is not part of it (the behaviour of K6-30 holds).
        var state = FunctionalBlockFixture.SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1);
        Assert.True(OperationConstructs.TryGetConstructPart(new CompoundOp([Harvest(Tree(0)), state]), out var statePart));
        Assert.Equal(state, Assert.Single(Assert.IsType<CompoundOp>(statePart).Components));
    }
}
