using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Items;

/// <summary>K6-30, contract of operations 35 (hosts of construct blocks), 36 (<see cref="ContainerRemoveOp"/>) and 37 (<see cref="ContainerConfigureOp"/>).</summary>
public sealed class ContainerLifecycleTests
{
    private static readonly CellPos _chestCell = FunctionalBlocks.ChestCell;

    [Fact]
    public void ContainerCreate_ConstructBlockHost_Succeeds()
    {
        var f = new FunctionalBlockFixture();

        var id = f.SetUpChest(_chestCell, size: 8, access: ContainerAccess.Private);

        var container = f.Container(id);
        Assert.Equal(ContainerKind.Chest, container.Kind);
        Assert.Equal(ContainerHost.ConstructBlock(ConstructId, _chestCell), container.Host);
        Assert.Equal(8, container.Size);
        Assert.Equal(ContainerAccess.Private, container.Access);
        Assert.Equal(1u, container.Version);
        Assert.Same(container, Assert.Single(f.HostedBy(_chestCell)));
    }

    [Fact]
    public void ContainerCreate_ConstructBlockHostEmptyCell_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.SetUpChest(_chestCell);
        var next = f.State.ContainerIds.PeekNext();

        f.AssertFails(ChestContainer(next, ConstructId, FunctionalBlocks.FreeCell));
        f.AssertFails(ChestContainer(next, new EntityId(99), _chestCell));
    }

    [Fact]
    public void ContainerCreate_SecondOfKindOnSameBlock_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.SetUpChest(_chestCell);
        var next = f.State.ContainerIds.PeekNext();

        f.AssertFails(ChestContainer(next, ConstructId, _chestCell));

        // Another kind may share the block (the stations of K6-32 have an input and an output).
        f.Apply(new ContainerCreateOp(next, ContainerKind.StationOutput, ContainerHost.ConstructBlock(ConstructId, _chestCell), Player1, ContainerAccess.Shared, 4));
        Assert.Equal(2, f.HostedBy(_chestCell).Count());
    }

    [Fact]
    public void ContainerRemove_Empty_RemovedAndHostFreed()
    {
        var f = new FunctionalBlockFixture();
        var id = f.SetUpChest(_chestCell);

        var applied = f.Apply(new CompoundOp([new ContainerRemoveOp(id), ClearState(_chestCell, FunctionalBlocks.ChestType)]));

        Assert.False(f.State.Containers.TryGet(id, out _));
        Assert.Empty(f.HostedBy(_chestCell));
        var change = Assert.Single(applied.ContainerChanges);
        Assert.Equal(id, change.Container);
        Assert.True(change.Removed);
        f.SetUpChest(_chestCell);
        Assert.Single(f.HostedBy(_chestCell));
    }

    [Fact]
    public void ContainerRemove_NotEmpty_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(PlaceBlock(FunctionalBlocks.FreeCell, FunctionalBlocks.ChestType));
        var emptyOne = f.SetUpChest(FunctionalBlocks.FreeCell);
        f.RemoveChest(FunctionalBlocks.FreeCell, emptyOne);
        var full = f.SetUpChest(_chestCell);
        f.Apply(Stone(full, 3, 5));

        f.AssertFails(new ContainerRemoveOp(full));
        Assert.True(f.State.Containers.TryGet(full, out _));
        f.AssertFails(new ContainerRemoveOp(new ContainerId(9999)));
    }

    [Fact]
    public void ContainerRemove_PlayerContainer_Fails()
    {
        var f = new FunctionalBlockFixture();
        var id = f.SetUpChest(_chestCell);
        f.RemoveChest(_chestCell, id);

        // Bag, hotbar and equipment of a player are empty here and still never disappear.
        f.AssertFails(new ContainerRemoveOp(WorldFactory.BagOf(1)));
        f.AssertFails(new ContainerRemoveOp(WorldFactory.HotbarOf(1)));
        f.AssertFails(new ContainerRemoveOp(WorldFactory.EquipmentOf(1)));
    }

    [Fact]
    public void ContainerConfigure_Access_VersionPlusOne()
    {
        var f = new FunctionalBlockFixture();
        var id = f.SetUpChest(_chestCell, size: 4);
        var version = f.Container(id).Version;

        var applied = f.Apply(new ContainerConfigureOp(id, 4, ContainerAccess.Private));

        Assert.Equal(ContainerAccess.Private, f.Container(id).Access);
        Assert.Equal(4, f.Container(id).Size);
        Assert.Equal(version + 1, f.Container(id).Version);
        Assert.Equal(id, Assert.Single(applied.ContainerChanges).Container);
        Assert.True(Assert.Single(applied.ContainerChanges).Configured);

        var withSlots = f.Apply(new CompoundOp([new ContainerConfigureOp(id, 8, ContainerAccess.Private), FunctionalBlockFixture.Stone(id, 5, 1)]));
        var change = Assert.Single(withSlots.ContainerChanges);
        Assert.True(change.Configured);
        Assert.Equal([(ushort)5], change.Slots);
        Assert.False(Assert.Single(f.Apply(FunctionalBlockFixture.Stone(id, 0, 1)).ContainerChanges).Configured);
        f.Apply(new ContainerConfigureOp(id, 6, ContainerAccess.Private));
        Assert.Equal(6, f.Container(id).Size);
        Assert.Equal(version + 4, f.Container(id).Version);
    }

    [Fact]
    public void ContainerConfigure_ShrinkOverItems_Fails()
    {
        var f = new FunctionalBlockFixture();
        var id = f.SetUpChest(_chestCell, size: 4);
        f.Apply(Stone(id, 3, 5));
        f.Apply(new ContainerConfigureOp(id, 8, ContainerAccess.Shared));
        Assert.Equal(8, f.Container(id).Size);

        f.AssertFails(new ContainerConfigureOp(id, 3, ContainerAccess.Shared));
        f.AssertFails(new ContainerConfigureOp(id, 0, ContainerAccess.Shared));
        f.AssertFails(new ContainerConfigureOp(id, 257, ContainerAccess.Shared));
        f.Apply(new ContainerConfigureOp(id, 4, ContainerAccess.Shared));
        Assert.Equal(4, f.Container(id).Size);
    }

    [Fact]
    public void ContainerConfigure_NoChange_Fails()
    {
        var f = new FunctionalBlockFixture();
        var id = f.SetUpChest(_chestCell, size: 4);
        f.Apply(new ContainerConfigureOp(id, 4, ContainerAccess.Private));

        f.AssertFails(new ContainerConfigureOp(id, 4, ContainerAccess.Private));
        f.AssertFails(new ContainerConfigureOp(new ContainerId(9999), 4, ContainerAccess.Private));
    }
}
