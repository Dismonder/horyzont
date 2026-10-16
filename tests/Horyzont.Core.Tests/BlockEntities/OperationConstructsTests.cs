using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.BlockEntities;

/// <summary>K6-30, D14: the construct part of an operation, used by replication, meshing and the client mirror.</summary>
public sealed class OperationConstructsTests
{
    [Fact]
    public void ConstructPart_PlaceChestCompound_ReturnsBlockSetOnly()
    {
        var block = PlaceBlock(FunctionalBlocks.FreeCell, FunctionalBlocks.ChestType);
        var compound = CompoundConstructTests.PlaceChestCompound(new ContainerId(4), FunctionalBlocks.FreeCell);

        Assert.True(OperationConstructs.TryGetConstructPart(compound, out var part));
        Assert.Equal(block, part);
        Assert.IsType<BlockSetOp>(part);

        var created = new ConstructCreateOp(
            new EntityId(5), FrameRef.Of(WorldFactory.HomePlanet), Vector3.Zero, Quaternion.Identity, default, BlockState.Create(FunctionalBlocks.ChestType, 0));
        Assert.True(OperationConstructs.TryGetConstructPart(new CompoundOp([new ContainerRemoveOp(new ContainerId(4)), created]), out var createdPart));
        Assert.Equal(created, createdPart);
    }

    [Fact]
    public void ConstructPart_ItemOnlyCompound_False()
    {
        var itemOnly = new CompoundOp([ItemFixture.Create(ItemFixture.BagSlot(0), ItemFixture.Stone, 2), new ContainerRemoveOp(new ContainerId(4))]);

        Assert.False(OperationConstructs.TryGetConstructPart(itemOnly, out var part));
        Assert.Null(part);
        Assert.False(OperationConstructs.TryGetConstructPart(ItemFixture.Create(ItemFixture.BagSlot(0), ItemFixture.Stone, 2), out _));

        // The top-level operations of a construct return themselves, the others (here a state of a block) do not concern the blocks of the mirror.
        var block = PlaceBlock(FunctionalBlocks.FreeCell, FunctionalBlocks.ChestType);
        Assert.True(OperationConstructs.TryGetConstructPart(block, out var self));
        Assert.Same(block, self);
        Assert.False(OperationConstructs.TryGetConstructPart(SetBedState(FunctionalBlocks.BedCell, PlayerId.None), out _));
    }

    [Fact]
    public void ConstructPart_DoorColumn_CompoundOfFunctionStates()
    {
        var low = SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1);
        var high = SetFunctionState(FunctionalBlocks.DoorHighCell, FunctionalBlocks.DoorType, 1);

        Assert.True(OperationConstructs.TryGetConstructPart(new CompoundOp([low, high]), out var part));
        var compound = Assert.IsType<CompoundOp>(part);
        Assert.Equal([low, high], compound.Components);
        Assert.True(OperationConstructs.TryGetConstructPart(low, out var single));
        Assert.Same(low, single);
    }

    [Fact]
    public void ConstructPart_TerrainCompound_False()
    {
        var dig = new TerrainEditOp(SimFixture.Planet, TerrainShape.Sphere, new Vector3(0, 4000, 0), 0.5f, TerrainEditMode.Remove, 0);

        // Control: the block component of a compound is found, the terrain component is not a construct part.
        var block = PlaceBlock(FunctionalBlocks.FreeCell, FunctionalBlocks.ChestType);
        Assert.True(OperationConstructs.TryGetConstructPart(new CompoundOp([ItemFixture.Create(ItemFixture.BagSlot(0), ItemFixture.Stone, 1), block]), out var part));
        Assert.Equal(block, part);

        Assert.False(OperationConstructs.TryGetConstructPart(new CompoundOp([ItemFixture.Create(ItemFixture.BagSlot(0), ItemFixture.Stone, 1), dig]), out _));
        Assert.False(OperationConstructs.TryGetConstructPart(dig, out _));
    }
}
