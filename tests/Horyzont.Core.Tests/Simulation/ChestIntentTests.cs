using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>
/// Chests, beds and lamps by intent: placing and removing them (K6-30 D7, D8, D10), item moves into chests in reach (D9) and the chest
/// settings of <see cref="BlockConfigIntent"/> (D18). Tests that expect <see cref="RejectReason.InvalidParameters"/> or
/// <see cref="RejectReason.ContainerLocked"/> first accept a valid twin of the intent: today both answers come out of the intents for
/// the wrong reason, so the refusal alone would prove nothing.
/// </summary>
public sealed class ChestIntentTests
{
    private static readonly ItemTypeId _stone = WorldFactory.Item(SurvivalContentFactory.StoneKey);

    private static FunctionalBlockFixture Fixture(int players = 1, bool withSurvival = true, ushort chestSlots = 4) =>
        ForIntents(players, withSurvival, chestSlots);

    private static PlaceBlockIntent PlaceOnFace(CellPos clicked, Face face, ushort type) => new(new ConstructFaceTarget(ConstructId, clicked, face), type, 0);

    /// <summary>Places on the top of the floor block at x = 4: the new block lands in (4, 1, 0).</summary>
    private static PlaceBlockIntent PlaceOnFloor(ushort type) => PlaceOnFace(FunctionalBlocks.FloorCells[4], Face.PosY, type);

    private static readonly CellPos _floorTop = new(4, 1, 0);

    private static PlaceBlockIntent PlaceOnTerrain(FunctionalBlockFixture f, ushort type)
    {
        var eye = f.Sim.Eye(Player1);
        var target = f.Sim.Ground(2f, 6f);
        return new PlaceBlockIntent(new TerrainTarget(SimFixture.Planet, eye, SimFixture.Direction(eye, target), -Vector3.UnitZ), type, 0);
    }

    private static RemoveBlockIntent Remove(CellPos cell) => new(ConstructId, cell);

    private static ItemMoveIntent Move(ContainerId from, int fromSlot, ContainerId to, int toSlot, int count, int expected) =>
        new(new SlotRef(from, (ushort)fromSlot), ItemMoveTarget.ToSlot(new SlotRef(to, (ushort)toSlot)), (ushort)count, _stone, (ushort)expected);

    private static BlockConfigIntent Access(CellPos cell, ContainerAccess access) => new(ConstructId, cell, new ContainerAccessConfig(access));

    [Fact]
    public void PlaceChest_OnTerrain_CompoundWithContainerAndEntity()
    {
        var f = Fixture();
        var container = f.State.ContainerIds.PeekNext();

        var applied = f.Accept(Player1, PlaceOnTerrain(f, FunctionalBlocks.ChestType));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(3, compound.Components.Count);
        var create = Assert.IsType<ContainerCreateOp>(compound.Components[0]);
        var entity = Assert.IsType<BlockEntitySetOp>(compound.Components[1]);
        var construct = Assert.IsType<ConstructCreateOp>(compound.Components[2]);
        Assert.Equal(
            new ContainerCreateOp(container, ContainerKind.Chest, ContainerHost.ConstructBlock(construct.Construct, construct.FirstCell), Player1, ContainerAccess.Shared, 4),
            create);
        Assert.Equal(new BlockEntitySetOp(construct.Construct, construct.FirstCell, FunctionalBlocks.ChestType, new ChestBlockState(container)), entity);
        Assert.Equal(FunctionalBlocks.ChestType, construct.Block.TypeId);
        Assert.True(f.State.Containers.TryGet(container, out var created));
        Assert.Equal(ContainerKind.Chest, created.Kind);
    }

    [Fact]
    public void PlaceChest_OnConstructFace_CompoundBlockSetLast()
    {
        var f = Fixture();
        var container = f.State.ContainerIds.PeekNext();
        var version = f.ConstructVersion;

        var applied = f.Accept(Player1, PlaceOnFloor(FunctionalBlocks.ChestType));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(3, compound.Components.Count);
        Assert.IsType<ContainerCreateOp>(compound.Components[0]);
        Assert.Equal(new BlockEntitySetOp(ConstructId, _floorTop, FunctionalBlocks.ChestType, new ChestBlockState(container)), compound.Components[1]);
        var set = Assert.IsType<BlockSetOp>(compound.Components[2]);
        Assert.Equal(ConstructId, set.Construct);
        Assert.Equal(_floorTop, set.Cell);
        Assert.Equal(FunctionalBlocks.ChestType, set.Block.TypeId);
        Assert.Equal(new ConstructChange(ConstructId, version + 1, false), Assert.Single(applied.ConstructChanges));
        Assert.True(f.HasState(_floorTop, out var state));
        Assert.Equal(new ChestBlockState(container), state);
    }

    [Fact]
    public void PlaceChest_SizeFromContent_CarriedInOp()
    {
        var f = Fixture(chestSlots: 8);

        var applied = f.Accept(Player1, PlaceOnFloor(FunctionalBlocks.ChestType));

        var create = Assert.IsType<ContainerCreateOp>(Assert.IsType<CompoundOp>(applied.Operation).Components[0]);
        Assert.Equal(8, create.Size);
        Assert.Equal(8, f.Container(create.Id).Size);
    }

    [Fact]
    public void PlaceLamp_InitialStateOn()
    {
        var f = Fixture();

        var applied = f.Accept(Player1, PlaceOnFloor(FunctionalBlocks.LampType));

        var set = Assert.IsType<BlockSetOp>(applied.Operation);
        Assert.Equal(FunctionalBlocks.LampType, set.Block.TypeId);
        Assert.Equal(1, set.Block.FunctionState);
        Assert.Equal(1, f.BlockAt(_floorTop).FunctionState);
    }

    [Fact]
    public void PlaceChest_WithoutSurvivalContent_InvalidParameters()
    {
        var f = Fixture(withSurvival: false);
        f.Accept(Player1, PlaceOnFloor(FunctionalBlocks.StoneType));

        f.AssertRejected(Player1, PlaceOnFace(_floorTop, Face.PosY, FunctionalBlocks.ChestType), RejectReason.InvalidParameters);
        f.AssertRejected(Player1, PlaceOnFace(_floorTop, Face.PosY, FunctionalBlocks.BedType), RejectReason.InvalidParameters);
    }

    [Fact]
    public void RemoveChest_Empty_Compound()
    {
        var f = Fixture();
        var container = f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);

        var applied = f.Accept(Player1, Remove(FunctionalBlocks.ChestCell));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(
            [
                new ContainerRemoveOp(container),
                ClearState(FunctionalBlocks.ChestCell, FunctionalBlocks.ChestType),
                new BlockClearOp(ConstructId, FunctionalBlocks.ChestCell),
            ],
            compound.Components);
        Assert.False(f.State.Containers.TryGet(container, out _));
        Assert.False(f.HasState(FunctionalBlocks.ChestCell, out _));
        Assert.True(f.BlockAt(FunctionalBlocks.ChestCell).IsEmpty);
    }

    [Fact]
    public void RemoveChest_NotEmpty_ContainerNotEmpty()
    {
        var f = Fixture();
        var container = f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        f.Give(container, 0, 3);

        f.AssertRejected(Player1, Remove(FunctionalBlocks.ChestCell), FunctionalReasons.ContainerNotEmpty);
        Assert.False(f.BlockAt(FunctionalBlocks.ChestCell).IsEmpty);
    }

    [Fact]
    public void RemovePrivateChest_NotOwner_NotOwner()
    {
        var f = Fixture(players: 2);
        f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1, ContainerAccess.Private);

        f.AssertRejected(Player2, Remove(FunctionalBlocks.ChestCell), FunctionalReasons.NotOwner);
        Assert.False(f.BlockAt(FunctionalBlocks.ChestCell).IsEmpty);
    }

    [Fact]
    public void RemoveBed_OwnerRespawnCleared()
    {
        var f = Fixture(players: 2);
        f.SetUpBed(FunctionalBlocks.BedCell, Player1);

        // Anyone may take a bed down (D10); the owner loses the respawn point.
        var applied = f.Accept(Player2, Remove(FunctionalBlocks.BedCell));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(
            [
                new RespawnPointSetOp(Player1, null),
                ClearState(FunctionalBlocks.BedCell, FunctionalBlocks.BedType),
                new BlockClearOp(ConstructId, FunctionalBlocks.BedCell),
            ],
            compound.Components);
        Assert.False(f.State.Progress.TryGet(Player1, out _));
        Assert.False(f.HasState(FunctionalBlocks.BedCell, out _));
        Assert.True(f.BlockAt(FunctionalBlocks.BedCell).IsEmpty);
    }

    [Fact]
    public void MoveBagToChest_InRange_Transfer()
    {
        var f = Fixture();
        var chest = f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        f.Give(Bag, 0, 10);

        var applied = f.Accept(Player1, Move(Bag, 0, chest, 0, 10, 10));

        Assert.Equal(new ItemTransferOp(new SlotRef(Bag, 0), new SlotRef(chest, 0), _stone, 10, 500), applied.Operation);
        Assert.Equal(new ItemStack(_stone, 10), f.Container(chest).GetSlot(0));
        Assert.True(f.Container(Bag).GetSlot(0).IsEmpty);
    }

    [Fact]
    public void MoveToChest_Beyond8m_OutOfReach()
    {
        var f = Fixture();
        var chest = f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        f.Give(Bag, 0, 10);
        f.Accept(Player1, Move(Bag, 0, chest, 0, 5, 10));
        f.Move(Player1, new Vector3(2f, 4001f, 15f));

        f.AssertRejected(Player1, Move(Bag, 0, chest, 0, 5, 5), RejectReason.OutOfReach);
        f.AssertRejected(Player1, Move(chest, 0, Bag, 1, 5, 5), RejectReason.OutOfReach);
    }

    [Fact]
    public void MoveToPrivateChestOfOther_ContainerLocked()
    {
        var f = Fixture(players: 2);
        var shared = f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        var privateChest = f.SetUpChestAt(_floorTop, Player1, ContainerAccess.Private);
        var bag2 = WorldFactory.BagOf(2);
        f.Give(bag2, 0, 10);
        f.Accept(Player2, Move(bag2, 0, shared, 0, 3, 10));

        f.AssertRejected(Player2, Move(bag2, 0, privateChest, 0, 3, 7), RejectReason.ContainerLocked);
        Assert.True(f.Container(privateChest).GetSlot(0).IsEmpty);
    }

    [Fact]
    public void ConfigureAccess_Owner_ContainerConfigureOp()
    {
        var f = Fixture();
        var chest = f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);

        var applied = f.Accept(Player1, Access(FunctionalBlocks.ChestCell, ContainerAccess.Private));

        Assert.Equal(new ContainerConfigureOp(chest, 4, ContainerAccess.Private), applied.Operation);
        Assert.Equal(ContainerAccess.Private, f.Container(chest).Access);
    }

    [Fact]
    public void ConfigureAccess_NotOwner_NotOwner()
    {
        var f = Fixture(players: 2);
        var chest = f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);

        f.AssertRejected(Player2, Access(FunctionalBlocks.ChestCell, ContainerAccess.Private), FunctionalReasons.NotOwner);
        Assert.Equal(ContainerAccess.Shared, f.Container(chest).Access);
    }

    [Fact]
    public void ConfigureAccess_Same_InvalidParameters()
    {
        var f = Fixture();
        f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        f.Accept(Player1, Access(FunctionalBlocks.ChestCell, ContainerAccess.Private));

        f.AssertRejected(Player1, Access(FunctionalBlocks.ChestCell, ContainerAccess.Private), RejectReason.InvalidParameters);
    }

    [Fact]
    public void ConfigureTeleportName_InvalidParameters()
    {
        var f = Fixture();
        f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        f.Accept(Player1, Access(FunctionalBlocks.ChestCell, ContainerAccess.Private));

        // The variant is reserved for K6-37: valid in the format, answered with invalid parameters here.
        f.AssertRejected(Player1, new BlockConfigIntent(ConstructId, FunctionalBlocks.ChestCell, new TeleportNameConfig("Base")), RejectReason.InvalidParameters);
        f.AssertRejected(Player1, Access(FunctionalBlocks.DoorLowCell, ContainerAccess.Shared), RejectReason.InvalidParameters);
    }
}
