using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.BlockEntities;

/// <summary>K6-30, table of the contract of operation 40 (<see cref="BlockEntitySetOp"/>). A refused operation changes nothing, also not the hash.</summary>
public sealed class BlockEntityOpsApplierTests
{
    private static readonly CellPos _chestCell = FunctionalBlocks.ChestCell;
    private static readonly CellPos _bedCell = FunctionalBlocks.BedCell;

    [Fact]
    public void BlockEntitySet_OnBlockOfExpectedType_StoresState()
    {
        var f = new FunctionalBlockFixture();
        var container = f.SetUpChest(_chestCell);

        f.Apply(SetBedState(_bedCell, Player1));

        Assert.Equal(2, f.State.BlockEntities.Count);
        Assert.True(f.HasState(_chestCell, out var chest));
        Assert.Equal(new ChestBlockState(container), chest);
        Assert.True(f.HasState(_bedCell, out var bed));
        Assert.Equal(new BedBlockState(Player1), bed);
        Assert.Equal(2, f.State.BlockEntities.CountOf(ConstructId));
    }

    [Fact]
    public void BlockEntitySet_WrongExpectedType_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetBedState(_bedCell, PlayerId.None));

        // The lamp cell holds a lamp, not a bed.
        f.AssertFails(SetBedState(FunctionalBlocks.LampCell, PlayerId.None));
        Assert.Equal(1, f.State.BlockEntities.Count);
    }

    [Fact]
    public void BlockEntitySet_EmptyCell_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetBedState(_bedCell, PlayerId.None));

        f.AssertFails(SetBedState(FunctionalBlocks.FreeCell, PlayerId.None));
        Assert.Equal(1, f.State.BlockEntities.Count);
    }

    [Fact]
    public void BlockEntitySet_KindChange_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetBedState(_bedCell, PlayerId.None));
        var hostedByBed = f.State.ContainerIds.PeekNext();

        // A bed keeps its kind: a chest state on the same cell is another kind (the container is created in the same compound).
        f.AssertFails(new CompoundOp(
        [
            ChestContainer(hostedByBed, ConstructId, _bedCell),
            new BlockEntitySetOp(ConstructId, _bedCell, FunctionalBlocks.BedType, new ChestBlockState(hostedByBed)),
        ]));
        Assert.True(f.HasState(_bedCell, out var state));
        Assert.Equal(new BedBlockState(PlayerId.None), state);
    }

    [Fact]
    public void BlockEntitySet_SameKindOtherFields_Replaces()
    {
        var f = new FunctionalBlockFixture(players: 2);
        f.Apply(SetBedState(_bedCell, PlayerId.None));

        f.Apply(SetBedState(_bedCell, Player2));

        Assert.True(f.HasState(_bedCell, out var bed));
        Assert.Equal(new BedBlockState(Player2), bed);
        Assert.Equal(1, f.State.BlockEntities.Count);
    }

    [Fact]
    public void BlockEntitySet_ChestWithForeignContainer_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.SetUpChest(_chestCell);
        f.Apply(PlaceBlock(FunctionalBlocks.FreeCell, FunctionalBlocks.ChestType));
        f.Apply(PlaceBlock(FunctionalBlocks.FreeCell2, FunctionalBlocks.ChestType));
        var elsewhere = f.SetUpChest(FunctionalBlocks.FreeCell2);

        // The container hosted by the player, a container hosted by another block, a container that does not exist.
        f.AssertFails(SetChestState(FunctionalBlocks.FreeCell, Bag));
        f.AssertFails(SetChestState(FunctionalBlocks.FreeCell, elsewhere));
        f.AssertFails(SetChestState(FunctionalBlocks.FreeCell, new ContainerId(9999)));
    }

    [Fact]
    public void BlockEntitySet_BedWithUnknownOwner_Fails()
    {
        var f = new FunctionalBlockFixture();

        f.AssertFails(SetBedState(_bedCell, new PlayerId(99)));
        Assert.Equal(0, f.State.BlockEntities.Count);

        // No owner is a valid, free bed.
        f.Apply(SetBedState(_bedCell, PlayerId.None));
        Assert.Equal(1, f.State.BlockEntities.Count);
    }

    [Fact]
    public void BlockEntitySet_EmptyStateWithoutEntity_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetBedState(_bedCell, PlayerId.None));

        f.AssertFails(ClearState(FunctionalBlocks.LampCell, FunctionalBlocks.LampType));
        Assert.Equal(1, f.State.BlockEntities.Count);
    }

    [Fact]
    public void BlockEntitySet_EmptyState_RemovesEntity()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetBedState(_bedCell, PlayerId.None));
        Assert.Equal(1, f.State.BlockEntities.Count);

        f.Apply(ClearState(_bedCell, FunctionalBlocks.BedType));

        Assert.Equal(0, f.State.BlockEntities.Count);
        Assert.False(f.HasState(_bedCell, out _));
    }

    [Fact]
    public void BlockEntitySet_ChestStateRemovedAlone_ContainerStays_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.SetUpChest(_chestCell);

        f.AssertFails(ClearState(_chestCell, FunctionalBlocks.ChestType));
        Assert.True(f.HasState(_chestCell, out _));
    }

    [Fact]
    public void Compound_ChestStateRemovedAndContainerRemovedLater_IsApplied()
    {
        var f = new FunctionalBlockFixture();
        var id = f.SetUpChest(_chestCell);

        f.Apply(new CompoundOp([ClearState(_chestCell, FunctionalBlocks.ChestType), new ContainerRemoveOp(id)]));

        Assert.False(f.HasState(_chestCell, out _));
        Assert.Empty(f.HostedBy(_chestCell));
    }

    [Fact]
    public void Compound_ChestStateRemovedContainerStays_Fails()
    {
        var f = new FunctionalBlockFixture();
        var id = f.SetUpChest(_chestCell);

        f.AssertFails(new CompoundOp([ClearState(_chestCell, FunctionalBlocks.ChestType), Stone(id, 1, 1)]));
        Assert.True(f.HasState(_chestCell, out _));
    }

    [Fact]
    public void ContainerCreate_ChestAloneWithoutState_Fails()
    {
        var f = new FunctionalBlockFixture();

        f.AssertFails(ChestContainer(f.State.ContainerIds.PeekNext(), ConstructId, _chestCell));
        Assert.Empty(f.HostedBy(_chestCell));
    }

    [Fact]
    public void ContainerRemove_ChestAloneStateStays_Fails()
    {
        var f = new FunctionalBlockFixture();
        var id = f.SetUpChest(_chestCell);

        f.AssertFails(new ContainerRemoveOp(id));
        Assert.True(f.HasState(_chestCell, out _));
        Assert.Single(f.HostedBy(_chestCell));
    }

    [Fact]
    public void Compound_ChestContainerThenState_IsApplied()
    {
        var f = new FunctionalBlockFixture();
        var id = f.State.ContainerIds.PeekNext();

        f.Apply(new CompoundOp([ChestContainer(id, ConstructId, _chestCell), SetChestState(_chestCell, id)]));

        Assert.True(f.HasState(_chestCell, out _));
        Assert.Single(f.HostedBy(_chestCell));
    }

    [Fact]
    public void BlockEntitySet_Over4096PerConstruct_Fails()
    {
        var f = new FunctionalBlockFixture();
        var cells = Enumerable.Range(0, 4097).Select(i => new CellPos(10 + (i % 64), 5 + ((i / 64) % 64), 2 + (i / 4096))).ToArray();
        foreach (var cell in cells)
        {
            f.Apply(PlaceBlock(cell, FunctionalBlocks.BedType));
        }

        foreach (var cell in cells.Take(4096))
        {
            f.Apply(SetBedState(cell, PlayerId.None));
        }

        Assert.Equal(4096, f.State.BlockEntities.CountOf(ConstructId));
        f.AssertFails(SetBedState(cells[4096], PlayerId.None));
        Assert.Equal(4096, f.State.BlockEntities.CountOf(ConstructId));
    }
}
